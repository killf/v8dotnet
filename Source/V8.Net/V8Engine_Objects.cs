﻿namespace V8.Net
{
    using System;
    using System.Linq;
    using System.Threading;

#if !(V1_1 || V2 || V3 || V3_5)
    using System.Dynamic;
#endif

    // ========================================================================================================================

    public unsafe partial class V8Engine
    {
        // --------------------------------------------------------------------------------------------------------------------

        internal IndexedObjectList<ObservableWeakReference<V8NativeObject>>  ObjectsInternal 
        {
            get
            {
                return m_objects;
            }
        }

        /// <summary>
        /// Holds an index of all the created objects.
        /// </summary>
        private readonly IndexedObjectList<ObservableWeakReference<V8NativeObject>> m_objects = new IndexedObjectList<ObservableWeakReference<V8NativeObject>>();

        internal ReaderWriterLock ObjectsLockerInternal
        {
            get
            {
                return m_objectsLocker;
            }
        }

        private readonly ReaderWriterLock m_objectsLocker = new ReaderWriterLock();

        // --------------------------------------------------------------------------------------------------------------------

        internal ObservableWeakReference<V8NativeObject> _GetObjectWeakReference(Int32 objectId) // (performs the lookup in a lock block)
        {
            using (m_objectsLocker.ReadLock()) { return m_objects[objectId]; } // (Note: if index is outside bounds, then null is returned.)
        }

        internal V8NativeObject _GetObjectAsIs(Int32 objectId) // (performs the object lookup in a lock block without causing a GC reset)
        {
            using (m_objectsLocker.ReadLock()) { var weakRef = m_objects[objectId]; return weakRef != null ? weakRef.Object : null; }
        }

        internal void _RemoveObjectWeakReference(Int32 objectId) // (performs the removal in a lock block)
        {
            using (m_objectsLocker.WriteLock()) { m_objects.Remove(objectId); }
        }

        // --------------------------------------------------------------------------------------------------------------------

        void _Initialize_ObjectTemplate()
        {
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Creates an uninitialized managed object ONLY (does not attempt to associate it with a JavaScript object, regardless of the supplied handle).
        /// <para>Warning: The managed wrapper is not yet initialized.  When returning the new managed object to the user, make sure to call
        /// '_ObjectInfo.Initialize()' first. Note however that new objects should only be initialized AFTER setup is completed so the users
        /// (developers) can write initialization code on completed objects (see source as example for 'FunctionTemplate.GetFunctionObject()').</para>
        /// </summary>
        /// <typeparam name="T">The wrapper type to create (such as V8ManagedObject).</typeparam>
        /// <param name="template">The managed template reference that owns the native object, if applicable.</param>
        /// <param name="handle">The handle to the native V8 object.</param>
        /// <param name="connectNativeObject">If true (the default), then a native function is called to associate the native V8 object with the new managed object.
        /// Set this to false if native V8 objects will be associated manually for special cases.  This parameter is ignored if no handle is given (hNObj == null).</param>
        internal T _CreateManagedObject<T>(ITemplate template, InternalHandle handle)
            where T : V8NativeObject, new()
        {
            return _CreateManagedObject<T>(template, handle, true);
        }

        /// <summary>
        /// Creates an uninitialized managed object ONLY (does not attempt to associate it with a JavaScript object, regardless of the supplied handle).
        /// <para>Warning: The managed wrapper is not yet initialized.  When returning the new managed object to the user, make sure to call
        /// '_ObjectInfo.Initialize()' first. Note however that new objects should only be initialized AFTER setup is completed so the users
        /// (developers) can write initialization code on completed objects (see source as example for 'FunctionTemplate.GetFunctionObject()').</para>
        /// </summary>
        /// <typeparam name="T">The wrapper type to create (such as V8ManagedObject).</typeparam>
        /// <param name="template">The managed template reference that owns the native object, if applicable.</param>
        /// <param name="handle">The handle to the native V8 object.</param>
        /// <param name="connectNativeObject">If true (the default), then a native function is called to associate the native V8 object with the new managed object.
        /// Set this to false if native V8 objects will be associated manually for special cases.  This parameter is ignored if no handle is given (hNObj == null).</param>
        internal T _CreateManagedObject<T>(ITemplate template, InternalHandle handle, bool connectNativeObject)
                where T : V8NativeObject, new()
        {
            T newObject;

            try
            {
                if (typeof(V8ManagedObject).IsAssignableFrom(typeof(T)) && template == null)
                    throw new InvalidOperationException("You've attempted to create the type '" + typeof(T).Name + "' which implements IV8ManagedObject without a template (ObjectTemplate). The native V8 engine only supports interceptor hooks for objects generated from object templates.  At the very least, you can derive from 'V8NativeObject' and use the 'SetAccessor()' method.");

                if (!handle.IsUndefined)
                    if (!handle.IsObjectType)
                        throw new InvalidOperationException("The specified handle does not represent an native V8 object.");
                    else
                        if (connectNativeObject && handle.HasObject)
                            throw new InvalidOperationException("Cannot create a managed object for this handle when one already exists. Existing objects will not be returned by 'Create???' methods to prevent initializing more than once.");

                newObject = new T
                {
                    _Engine = this,
                    Template = template,
                    Handle = handle
                };

                using (m_objectsLocker.WriteLock()) // (need a lock because of the worker thread)
                {
                    newObject.ID = m_objects.Add(new ObservableWeakReference<V8NativeObject>(newObject));
                }

                if (!handle.IsUndefined)
                {
                    if (connectNativeObject)
                    {
                        try
                        {
                            var objectTemplate = template as ObjectTemplate;
                            var templateProxy = (objectTemplate != null)
                                ? (void*) objectTemplate.NativeObjectTemplateProxy
                                : (template is FunctionTemplate)
                                    ? ((FunctionTemplate) template).NativeFunctionTemplateProxy
                                    : null;

                            V8NetProxy.ConnectObject(handle, newObject.ID, templateProxy);

                            /* The V8 object will have an associated internal field set to the index of the created managed object above for quick lookup.  This index is used
                             * to locate the associated managed object when a call-back occurs. The lookup is a fast O(1) operation using the custom 'IndexedObjectList' manager.
                             */
                        }
                        catch (Exception)
                        {
                            // ... something went wrong, so remove the new managed object ...
                            _RemoveObjectWeakReference(newObject.ID);
                            handle.ObjectId = -1; // (existing ID no longer valid)
                            throw;
                        }
                    }
                }
            }
            finally
            {
                handle._DisposeIfFirst();
            }

            return newObject;
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Gets the managed object that wraps the native V8 object for the specific handle.
        /// <para>Warning: You MUST pass a handle for objects only created from this V8Engine instance, otherwise you may get errors, or a wrong object (without error).</para>
        /// </summary>
        /// <typeparam name="T">You can derive your own object from V8NativeObject, or implement IV8NativeObject yourself.
        /// In either case, you can specify the type here to have it created for new object handles.</typeparam>
        /// <param name="handle">A handle to a native object that contains a valid managed object ID.</param>
        /// <param name="createIfNotFound">If true, then an IV8NativeObject of type 'T' will be created if an existing IV8NativeObject object cannot be found, otherwise 'null' is returned.</param>
        /// <param name="initializeOnCreate">If true (default) then then 'IV8NativeObject.Initialize()' is called on the created wrapper.</param>
        public T GetObject<T>(InternalHandle handle)
            where T : V8NativeObject, new()
        {
            return GetObject<T>(handle, true, true);
        }

        /// <summary>
        /// Gets the managed object that wraps the native V8 object for the specific handle.
        /// <para>Warning: You MUST pass a handle for objects only created from this V8Engine instance, otherwise you may get errors, or a wrong object (without error).</para>
        /// </summary>
        /// <typeparam name="T">You can derive your own object from V8NativeObject, or implement IV8NativeObject yourself.
        /// In either case, you can specify the type here to have it created for new object handles.</typeparam>
        /// <param name="handle">A handle to a native object that contains a valid managed object ID.</param>
        /// <param name="createIfNotFound">If true, then an IV8NativeObject of type 'T' will be created if an existing IV8NativeObject object cannot be found, otherwise 'null' is returned.</param>
        /// <param name="initializeOnCreate">If true (default) then then 'IV8NativeObject.Initialize()' is called on the created wrapper.</param>
        public T GetObject<T>(InternalHandle handle, bool createIfNotFound, bool initializeOnCreate)
            where T : V8NativeObject, new()
        {
            return _GetObject<T>(null, handle, createIfNotFound, initializeOnCreate, true);
        }

        /// <summary>
        /// Returns a 'V8NativeObject' or 'V8Function' object based on the handle.
        /// </summary>
        public V8NativeObject GetObject(InternalHandle handle)
        {
            return GetObject(handle, true, true);
        }

        /// <summary>
        /// Returns a 'V8NativeObject' or 'V8Function' object based on the handle.
        /// </summary>
        public V8NativeObject GetObject(InternalHandle handle, bool createIfNotFound, bool initializeOnCreate)
        {
            if (handle.IsFunction)
                return GetObject<V8Function>(handle, createIfNotFound, initializeOnCreate);
            else
                return GetObject<V8NativeObject>(handle, createIfNotFound, initializeOnCreate); 
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Same as "GetObject()", but used internally for getting objects that are associated with templates (such as getting function prototype objects).
        /// </summary>
        internal T _GetObject<T>(ITemplate template, InternalHandle handle)
            where T : V8NativeObject, new()
        {
            return _GetObject<T>(template, handle, true, true, true);
        }

        /// <summary>
        /// Same as "GetObject()", but used internally for getting objects that are associated with templates (such as getting function prototype objects).
        /// </summary>
        internal T _GetObject<T>(ITemplate template, InternalHandle handle, bool createIfNotFound, bool initializeOnCreate, bool connectNativeObject)
            where T : V8NativeObject, new()
        {
            T obj = null;

            try
            {
                if (handle.IsEmpty)
                    return null;

                if (handle.Engine != this)
                    throw new InvalidOperationException("The specified handle was not generated from this V8Engine instance.");

                var weakRef = _GetObjectWeakReference(handle.ObjectId); // (if out of bounds or invalid, this will simply return null)
                if (weakRef != null)
                {
                    obj = weakRef.Reset() as T;
                    // ReSharper disable once UseMethodIsInstanceOfType
                    // ReSharper disable once UseIsOperator.1
                    if (obj != null && !typeof(T).IsAssignableFrom(obj.GetType()))
                        throw new InvalidCastException("The existing object of type '" + obj.GetType().Name + "' cannot be converted to type '" + typeof(T).Name + "'.");
                }

                if (obj == null && createIfNotFound)
                {
                    handle.ObjectId = -1; // (managed object doesn't exist [perhaps GC'd], so reset the ID)
                    obj = _CreateObject<T>(template, handle.PassOn(), initializeOnCreate, connectNativeObject);
                }
            }
            finally
            {
                handle._DisposeIfFirst();
            }

            return obj;
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns an object based on its ID (an object ID is simply an index value, so the lookup is fast, but it does not protect the object from
        /// garbage collection).
        /// <para>Note: If the ID is invalid, or the managed object has been garbage collected, then this will return null (no errors will occur).</para>
        /// <para>WARNING: Do not rely on this method unless you are sure the managed object is persisted. It's very possible for an object to be deleted and a
        /// new object put in the same place as identified by the same ID value. As long as you keep a reference/handle, or perform no other V8.NET actions
        /// between the time you read an object's ID, and the time this method is called, then you can safely use this method.</para>
        /// </summary>
        public V8NativeObject GetObjectById(int objectId)
        { 
            var weakRef = m_objects[objectId]; 
            return weakRef != null ? weakRef.Reset() : null; 
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns all the objects associated with a template reference.
        /// </summary>
        public V8NativeObject[] GetObjects(ITemplate template)
        {
            using (m_objectsLocker.ReadLock())
            {
                return (from o in m_objects where o.Object.Template == template select o.Object).ToArray();
            }
            // (WARNING: cannot enumerate objects in this block as it may cause a deadlock - 'lock (_Objects){}' can conflict with the finalizer thread if 'o.Object' blocks to wait on it)
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================
}
