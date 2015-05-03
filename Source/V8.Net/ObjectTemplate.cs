namespace V8.Net
{
    using System;
    using System.Collections.Generic;

    // ========================================================================================================================

    public interface ITemplate
    {
        /// <summary>
        /// The V8Engine instance associated with this template.
        /// </summary>
        V8Engine Engine { get; }
    }

    internal interface ITemplateInternal
    {
        uint ReferenceCountInternal { get; set; }
    }

    // ========================================================================================================================

    public unsafe abstract class TemplateBase<TObjectType> : ITemplate, ITemplateInternal where TObjectType : class, IV8NativeObject
    {
        // --------------------------------------------------------------------------------------------------------------------

        public V8Engine Engine
        {
            get;
            protected set;
        }


        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns the parent to this template, if any.
        /// This is currently only set on object template instances associated with function templates (where {FunctionTemplate} is the parent).
        /// </summary>
        public ITemplate Parent
        {
            get { return m_parent; }
            internal set
            {
                if (m_parent != null) ((ITemplateInternal)this).ReferenceCountInternal--;
                m_parent = value;
                if (m_parent != null) ((ITemplateInternal)this).ReferenceCountInternal++;
            }
        }
        private ITemplate m_parent;

        /// <summary>
        /// The number of objects that reference this object.
        /// This is required because of the way the GC resets all weak references to null, and finalizes in no special order.
        /// Dependent objects are required to update this when they are finally collected (as some may become re-registered with the finalizer).
        /// </summary>
        uint ITemplateInternal.ReferenceCountInternal { get; set; }

        // --------------------------------------------------------------------------------------------------------------------

        private List<Delegate> m_nativeCallbacks; // (if not stored here, then delegates will be GC'd and callbacks from native code will fail)

        /// <summary>
        /// Keeps callback delegates alive.
        /// <para>If delegates are used as callbacks (for reverse P/Invoke), then they will become GC'd if there's no managed reference keeping them alive.</para>
        /// </summary>
        protected T _SetDelegate<T>(T d)
        {
            if (m_nativeCallbacks == null)
                m_nativeCallbacks = new List<Delegate>();

            m_nativeCallbacks.Add((Delegate)(object)d);

            return d;
        }

        // --------------------------------------------------------------------------------------------------------------------

        protected TemplateBase()
        {
        }

        protected abstract void OnInitialized();

        // --------------------------------------------------------------------------------------------------------------------

        protected HandleProxy* _NamedPropertyGetter(string propertyName, ref ManagedAccessorInfo info)
        {
            try
            {
                var obj = Engine._GetObjectWeakReference(info.ManagedObjectID);
                if (obj == null)
                    return null;
                var mo = obj.Reset() as IV8ManagedObject; // (this acts also as a test because native object wrappers are also supported)
                return mo != null ? mo.NamedPropertyGetter(ref propertyName) : null;
            }
            catch (Exception ex)
            {
                return Engine.CreateError(ex.GetFullErrorMessage(), JSValueType.ExecutionError);
            }
        }

        protected HandleProxy* _NamedPropertySetter(string propertyName, HandleProxy* value, ref ManagedAccessorInfo info)
        {
            try
            {
                using (InternalHandle hValue = new InternalHandle(value, false))
                {
                    var obj = Engine._GetObjectWeakReference(info.ManagedObjectID);
                    if (obj == null)
                        return null;
                    var mo = obj.Reset() as IV8ManagedObject;
                    return mo != null ? mo.NamedPropertySetter(ref propertyName, hValue, V8PropertyAttributes.Undefined) : null;
                }
            }
            catch (Exception ex)
            {
                return Engine.CreateError(ex.GetFullErrorMessage(), JSValueType.ExecutionError);
            }
        }

        protected V8PropertyAttributes _NamedPropertyQuery(string propertyName, ref ManagedAccessorInfo info)
        {
            try
            {
                var obj = Engine._GetObjectWeakReference(info.ManagedObjectID);
                if (obj == null)
                    return V8PropertyAttributes.Undefined;
                var mo = obj.Reset() as IV8ManagedObject;
                var result = mo != null ? mo.NamedPropertyQuery(ref propertyName) : null;
                return result ?? V8PropertyAttributes.Undefined;
            }
            catch
            {
                return V8PropertyAttributes.Undefined; // TODO: Need a better way to marshal/pass these exception object instances (themselves) across the native boundary for the underlying engine instance)
            }
        }

        protected int _NamedPropertyDeleter(string propertyName, ref ManagedAccessorInfo info)
        {
            try
            {
                var obj = Engine._GetObjectWeakReference(info.ManagedObjectID);
                if (obj == null)
                    return -1;
                var mo = obj.Reset() as IV8ManagedObject;
                var result = mo != null ? mo.NamedPropertyDeleter(ref propertyName) : null;
                if (result != null) return result.Value ? 1 : 0;
                else return -1; // (not intercepted, so perform default action)
            }
            catch
            {
                return -1;
            }
        }

        protected HandleProxy* _NamedPropertyEnumerator(ref ManagedAccessorInfo info)
        {
            try
            {
                var obj = Engine._GetObjectWeakReference(info.ManagedObjectID);
                if (obj == null)
                    return null;
                var mo = obj.Reset() as IV8ManagedObject;
                return mo != null ? mo.NamedPropertyEnumerator() : null;
            }
            catch (Exception ex)
            {
                return Engine.CreateError(ex.GetFullErrorMessage(), JSValueType.ExecutionError);
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        protected HandleProxy* _IndexedPropertyGetter(Int32 index, ref ManagedAccessorInfo info)
        {
            try
            {
                var obj = Engine._GetObjectWeakReference(info.ManagedObjectID);
                if (obj == null)
                    return null;
                var mo = obj.Reset() as IV8ManagedObject;
                return mo != null ? mo.IndexedPropertyGetter(index) : null;
            }
            catch (Exception ex)
            {
                return Engine.CreateError(ex.GetFullErrorMessage(), JSValueType.ExecutionError);
            }
        }

        protected HandleProxy* _IndexedPropertySetter(Int32 index, HandleProxy* value, ref ManagedAccessorInfo info)
        {
            try
            {
                using (InternalHandle hValue = new InternalHandle(value, false))
                {
                    var obj = Engine._GetObjectWeakReference(info.ManagedObjectID);
                    if (obj == null)
                        return null;
                    var mo = obj.Reset() as IV8ManagedObject;
                    return mo != null ? mo.IndexedPropertySetter(index, hValue) : null;
                }
            }
            catch (Exception ex)
            {
                return Engine.CreateError(ex.GetFullErrorMessage(), JSValueType.ExecutionError);
            }
        }

        protected V8PropertyAttributes _IndexedPropertyQuery(Int32 index, ref ManagedAccessorInfo info)
        {
            try
            {
                var obj = Engine._GetObjectWeakReference(info.ManagedObjectID);
                if (obj == null)
                    return V8PropertyAttributes.Undefined;
                var mo = obj.Reset() as IV8ManagedObject;
                var result = mo != null ? mo.IndexedPropertyQuery(index) : null;
                if (result != null) return result.Value;
                else return V8PropertyAttributes.Undefined; // (not intercepted, so perform default action)
            }
            catch
            {
                return V8PropertyAttributes.Undefined;
            }
        }

        protected int _IndexedPropertyDeleter(Int32 index, ref ManagedAccessorInfo info)
        {
            try
            {
                var obj = Engine._GetObjectWeakReference(info.ManagedObjectID);
                if (obj == null)
                    return -1;
                var mo = obj.Reset() as IV8ManagedObject;
                var result = mo != null ? mo.IndexedPropertyDeleter(index) : null;
                if (result != null) return result.Value ? 1 : 0;
                else return -1; // (not intercepted, so perform default action)
            }
            catch
            {
                return -1;
            }
        }

        protected HandleProxy* _IndexedPropertyEnumerator(ref ManagedAccessorInfo info)
        {
            try
            {
                var obj = Engine._GetObjectWeakReference(info.ManagedObjectID);
                if (obj == null)
                    return null;
                var mo = obj.Reset() as IV8ManagedObject;
                return mo != null ? mo.IndexedPropertyEnumerator() : null;
            }
            catch (Exception ex)
            {
                return Engine.CreateError(ex.GetFullErrorMessage(), JSValueType.ExecutionError);
            }
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================

    public unsafe class ObjectTemplate : TemplateBase<IV8ManagedObject>, IFinalizable
    {
        public NativeObjectTemplateProxy* NativeObjectTemplateProxy
        {
            get;
            private set;
        }

        public ObjectTemplate()
        {
        }

        internal void _Initialize(V8Engine v8EngineProxy)
        {
            _Initialize(v8EngineProxy, true);
        }

        internal void _Initialize(V8Engine v8EngineProxy, bool registerPropertyInterceptors)
        {
            _Initialize(v8EngineProxy,
                V8NetProxy.CreateObjectTemplateProxy(v8EngineProxy.NativeV8EngineProxy), // (create a corresponding native object)
                registerPropertyInterceptors
            );
        }

        internal void _Initialize(V8Engine v8EngineProxy, NativeObjectTemplateProxy* nativeObjectTemplateProxy)
        {
            _Initialize(v8EngineProxy, nativeObjectTemplateProxy, true);
        }

        internal void _Initialize(V8Engine v8EngineProxy, NativeObjectTemplateProxy* nativeObjectTemplateProxy, bool registerPropertyInterceptors)
        {
            if (v8EngineProxy == null)
                throw new ArgumentNullException("v8EngineProxy");

            if (nativeObjectTemplateProxy == null)
                throw new ArgumentNullException("nativeObjectTemplateProxy");

            Engine = v8EngineProxy;

            NativeObjectTemplateProxy = nativeObjectTemplateProxy;

            if (registerPropertyInterceptors)
            {
                RegisterNamedPropertyInterceptors();
                RegisterIndexedPropertyInterceptors();
            }

            OnInitialized();
        }

        /// <summary>
        /// Called when the object is initialized instance is ready for use.
        /// </summary>
        protected override void OnInitialized()
        {
        }

        ~ObjectTemplate()
        {
            if (!((IFinalizable)this).CanFinalize)
                lock (Engine.ObjectsToFinalizeInternal)
                {
                    Engine.ObjectsToFinalizeInternal.Add(this);
                    GC.ReRegisterForFinalize(this);
                }
        }

        bool IFinalizable.CanFinalize { get; set; }

        void IFinalizable.DoFinalize()
        {
            if (((ITemplateInternal)this).ReferenceCountInternal == 0
            && Engine.GetObjects(this).Length == 0
            && Parent != null && Engine.GetObjects(Parent).Length == 0)
                Dispose();
        }

        public void Dispose() // TODO: !!! This will cause issues if removed while the native object exists. !!!
        {
            if (NativeObjectTemplateProxy != null)
            {
                Engine._ClearAccessors(NativeObjectTemplateProxy->ObjectID);

                V8NetProxy.DeleteObjectTemplateProxy(NativeObjectTemplateProxy); // (delete the corresponding native object as well; WARNING: This is done on the GC thread!)
                NativeObjectTemplateProxy = null;
            }

            ((IFinalizable)this).CanFinalize = true;
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns true if this function template has any name-based interceptors (callbacks) registered.
        /// </summary>
        public bool NamedPropertyInterceptorsRegistered { get; internal set; }

        /// Returns true if this function template has any index-based interceptors (callbacks) registered.
        /// <para>Note: This is numerical indexes only.</para>
        /// </summary>
        public bool IndexedPropertyInterceptorsRegistered { get; internal set; }

        /// <summary>
        /// Registers handlers that intercept access to properties on ALL objects created by this template.  The native V8 engine only supports this on 'ObjectTemplate's.
        /// </summary>
        public void RegisterNamedPropertyInterceptors()
        {
            if (!NamedPropertyInterceptorsRegistered)
            {
                V8NetProxy.RegisterNamedPropertyHandlers(NativeObjectTemplateProxy,
                    _SetDelegate<ManagedNamedPropertyGetter>(_NamedPropertyGetter),
                    _SetDelegate<ManagedNamedPropertySetter>(_NamedPropertySetter),
                    _SetDelegate<ManagedNamedPropertyQuery>(_NamedPropertyQuery),
                    _SetDelegate<ManagedNamedPropertyDeleter>(_NamedPropertyDeleter),
                    _SetDelegate<ManagedNamedPropertyEnumerator>(_NamedPropertyEnumerator));

                NamedPropertyInterceptorsRegistered = true;
            }
        }

        /// <summary>
        /// Registers handlers that intercept access to properties on ALL objects created by this template.  The native V8 engine only supports this on 'ObjectTemplate's.
        /// </summary>
        public void RegisterIndexedPropertyInterceptors()
        {
            if (!IndexedPropertyInterceptorsRegistered)
            {
                V8NetProxy.RegisterIndexedPropertyHandlers(NativeObjectTemplateProxy,
                    _SetDelegate<ManagedIndexedPropertyGetter>(_IndexedPropertyGetter),
                    _SetDelegate<ManagedIndexedPropertySetter>(_IndexedPropertySetter),
                    _SetDelegate<ManagedIndexedPropertyQuery>(_IndexedPropertyQuery),
                    _SetDelegate<ManagedIndexedPropertyDeleter>(_IndexedPropertyDeleter),
                    _SetDelegate<ManagedIndexedPropertyEnumerator>(_IndexedPropertyEnumerator));

                IndexedPropertyInterceptorsRegistered = true;
            }
        }

        /// <summary>
        /// Unregisters handlers that intercept access to properties on ALL objects created by this template.  See <see cref="RegisterNamedPropertyInterceptors()"/> and <see cref="RegisterIndexedPropertyInterceptors()"/>.
        /// </summary>
        public void UnregisterPropertyInterceptors()
        {
            if (NamedPropertyInterceptorsRegistered)
            {
                V8NetProxy.UnregisterNamedPropertyHandlers(NativeObjectTemplateProxy);

                V8NetProxy.UnregisterIndexedPropertyHandlers(NativeObjectTemplateProxy);

                NamedPropertyInterceptorsRegistered = false;
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Registers an invoke handler on the underlying native ObjectTemplate instance, which allows the object to be called like a method.
        /// </summary>
        /// <param name="callback">A callback that gets invoked </param>
        public void RegisterInvokeHandler(JSFunction callback)
        {
            V8NetProxy.RegisterInvokeHandler(NativeObjectTemplateProxy, (managedObjectId, isConstructCall, _this, args, argCount)
                => FunctionTemplate._CallBack(managedObjectId, isConstructCall, _this, args, argCount, callback));
            Engine._StoreAccessor(NativeObjectTemplateProxy->ObjectID, "$__InvokeHandler", callback);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Creates an object of the specified type and returns it.  A V8 object is also created and associated with it.
        /// <para>Performance note: Creating 'V8NativeObject' type objects are allowed, but an object template is not needed for those.  If you create a
        /// 'V8NativeObject' object from a template, it simply wraps the native object create by the template, and property interceptors (call-backs) are still
        /// triggered.  While native objects are faster than managed ones, creating 'V8NativeObject' objects using 'V8Engine.CreateObject()' does not use
        /// interceptors and is many times faster than template objects.  If it is desired to create 'V8NativeObject' objects from templates, consider calling
        /// '<seealso cref="UnregisterPropertyInterceptors()"/>' on the object template to make them the same speed as if 'V8Engine.CreateObject()' was used.</para>
        /// </summary>
        /// <typeparam name="T">The type of managed object to create, which must implement 'IV8NativeObject',</typeparam>
        /// <param name="initialize">If true (default) then then 'IV8NativeObject.Initialize()' is called on the created object before returning.</param>
        public T CreateObject<T>() where T : V8NativeObject, new()
        {
            return CreateObject<T>(true);
        }

        /// <summary>
        /// Creates an object of the specified type and returns it.  A V8 object is also created and associated with it.
        /// <para>Performance note: Creating 'V8NativeObject' type objects are allowed, but an object template is not needed for those.  If you create a
        /// 'V8NativeObject' object from a template, it simply wraps the native object create by the template, and property interceptors (call-backs) are still
        /// triggered.  While native objects are faster than managed ones, creating 'V8NativeObject' objects using 'V8Engine.CreateObject()' does not use
        /// interceptors and is many times faster than template objects.  If it is desired to create 'V8NativeObject' objects from templates, consider calling
        /// '<seealso cref="UnregisterPropertyInterceptors()"/>' on the object template to make them the same speed as if 'V8Engine.CreateObject()' was used.</para>
        /// </summary>
        /// <typeparam name="T">The type of managed object to create, which must implement 'IV8NativeObject',</typeparam>
        /// <param name="initialize">If true (default) then then 'IV8NativeObject.Initialize()' is called on the created object before returning.</param>
        public T CreateObject<T>(bool initialize) where T : V8NativeObject, new()
        {
            if (Engine == null)
                throw new InvalidOperationException("You must create object templates by calling one of the 'V8Engine.CreateObjectTemplate()' overloads.");

            if (NativeObjectTemplateProxy == null)
                throw new InvalidOperationException("This managed object template is either not initialized, or does not support creating V8 objects.");

            // ... create object locally first and index it ...

            var obj = Engine._CreateManagedObject<T>(this, InternalHandle.Empty);

            // ... create the native object and associated it to the managed wrapper ...

            try
            {
                obj.Handle._Set(V8NetProxy.CreateObjectFromTemplate(NativeObjectTemplateProxy, obj.Id));
                // (note: setting '_NativeObject' also updates it's '_ManagedObject' field if necessary.
            }
            catch (Exception)
            {
                // ... something went wrong, so remove the new managed object ...
                Engine._RemoveObjectWeakReference(obj.Id);
                throw;
            }

            if (initialize)
                obj.Initialize(false, null);

            return obj;
        }

        /// <summary>
        /// See <see cref="CreateObject{T}()"/>
        /// </summary>
        /// <param name="initialize">If true (default) then then 'IV8NativeObject.Initialize()' is called on the created object before returning.</param>
        public V8ManagedObject CreateObject()
        {
            return CreateObject(true);
        }

        /// <summary>
        /// See <see cref="CreateObject{T}()"/>.
        /// </summary>
        /// <param name="initialize">If true (default) then then 'IV8NativeObject.Initialize()' is called on the created object before returning.</param>
        public V8ManagedObject CreateObject(bool initialize)
        {
            return CreateObject<V8ManagedObject>(initialize);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls the V8 'Set()' function on the underlying native object template to set properties that will exist on all objects created from this template.
        /// </summary>
        public void SetProperty(string name, InternalHandle value)
        {
            SetProperty(name, value, V8PropertyAttributes.None);
        }

        /// <summary>
        /// Calls the V8 'Set()' function on the underlying native object template to set properties that will exist on all objects created from this template.
        /// </summary>
        public void SetProperty(string name, InternalHandle value, V8PropertyAttributes attributes)
        {
            try
            {
                if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException("name");

                V8NetProxy.SetObjectTemplateProperty(NativeObjectTemplateProxy, name, value, attributes);
            }
            finally
            {
                value._DisposeIfFirst();
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls the V8 'SetAccessor()' function on the underlying native 'v8::ObjectTenplate' instance to create a property that is controlled by "getter" and "setter" callbacks.
        /// <para>Note: This is template related, which means all objects created from this template will be affected by these special properties.</para>
        /// </summary>
        public void SetAccessor(string name,
            V8NativeObjectPropertyGetter getter, V8NativeObjectPropertySetter setter)
        {
            SetAccessor(name, getter, setter, V8PropertyAttributes.None, V8AccessControl.Default);
        }

        /// <summary>
        /// Calls the V8 'SetAccessor()' function on the underlying native 'v8::ObjectTenplate' instance to create a property that is controlled by "getter" and "setter" callbacks.
        /// <para>Note: This is template related, which means all objects created from this template will be affected by these special properties.</para>
        /// </summary>
        public void SetAccessor(string name,
            V8NativeObjectPropertyGetter getter, V8NativeObjectPropertySetter setter,
            V8PropertyAttributes attributes, V8AccessControl access)
        {
            if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException("name");

            var engine = Engine;

            V8NetProxy.SetObjectTemplateAccessor(NativeObjectTemplateProxy, -1, name,
                   Engine._StoreAccessor<ManagedAccessorGetter>(NativeObjectTemplateProxy->ObjectID, "get_" + name, (HandleProxy* _this, string propertyName) =>
                   {
                       try
                       {
                           using (InternalHandle hThis = _this) { return getter != null ? getter(hThis, propertyName) : null; }
                       }
                       catch (Exception ex)
                       {
                           return engine.CreateError(ex.GetFullErrorMessage(), JSValueType.ExecutionError);
                       }
                   }),
                   Engine._StoreAccessor<ManagedAccessorSetter>(NativeObjectTemplateProxy->ObjectID, "set_" + name, (HandleProxy* _this, string propertyName, HandleProxy* value) =>
                   {
                       try
                       {
                           using (InternalHandle hThis = _this) { return setter != null ? setter(hThis, propertyName, value) : null; }
                       }
                       catch (Exception ex)
                       {
                           return engine.CreateError(ex.GetFullErrorMessage(), JSValueType.ExecutionError);
                       }
                   }),
                   access, attributes);
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================
}
