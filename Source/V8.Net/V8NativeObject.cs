namespace V8.Net
{
    using System;

#if !(V1_1 || V2 || V3 || V3_5)
    using System.Dynamic;
    using System.Linq.Expressions;
#endif

    // ========================================================================================================================

    

    /// <summary>
    /// Represents a basic JavaScript object. This class wraps V8 functionality for operations required on any native V8 object (including managed ones).
    /// <para>This class implements 'DynamicObject' to make setting properties a bit easier.</para>
    /// </summary>
    public unsafe class V8NativeObject : IHandleBased, IV8Object, IV8NativeObject, IDynamicMetaObjectProvider, IFinalizable
    {
        // --------------------------------------------------------------------------------------------------------------------

        private const string ValueNotAnObjectErrorMessage = "The handle {0} does not represent a JavaScript object.";

        /// <summary>
        /// A reference to the V8Engine instance that owns this object.
        /// The default implementation for 'V8NativeObject' is to cache and return 'base.Engine', since it inherits from 'Handle'.
        /// </summary>
        public V8Engine Engine { get { return EngineInternal ?? (EngineInternal = HandleInternal.Engine); } }
        internal V8Engine EngineInternal;

        public Handle AsHandle() { return HandleInternal; }
        public InternalHandle AsInternalHandle { get { return HandleInternal.HandleInternal; } }
        public V8NativeObject Object { get { return this; } }

        /// <summary>
        /// The V8.NET ObjectTemplate or FunctionTemplate instance associated with this object, if any, or null if this object was not created using a V8.NET template.
        /// </summary>
        public ITemplate Template
        {
            get { return m_template; }
            internal set
            {
                if (m_template != null) ((ITemplateInternal)m_template).ReferenceCountInternal--;
                m_template = value;
                if (m_template != null) ((ITemplateInternal)m_template).ReferenceCountInternal++;
            }
        }
        private ITemplate m_template;

        /// <summary>
        /// The V8.NET managed object ID used to track this object instance on both the native and managed sides.
        /// </summary>
        public Int32 Id
        {
            get { var id = HandleInternal.ObjectId; return id < 0 ? IdInternal ?? id : id; } // (this attempts to return the underlying managed object ID of the handle proxy, or the local ID if -1)
            internal set
            {
                HandleInternal.ObjectId = value;
                IdInternal = value;
            } // (once set, the managed object will be fixed to the ID as long as the underlying handle has a handle-based object ID less than 0)
        }
        internal Int32? IdInternal; // ('_ID' is used within object collection to determine if the worker needs to get involved [{handle}.ObjectID cannot be used as it may call into the native side])

        /// <summary>
        /// Another object of the same interface to direct actions to (such as 'Initialize()').
        /// If the generic type 'V8NativeObject&lt;T>' is used, then this is set to an instance of "T", otherwise this is set to "this" instance.
        /// </summary>
        public IV8NativeObject Proxy { get { return ProxyInternal; } }
        internal IV8NativeObject ProxyInternal; // (Note: MUST NEVER BE NULL)

        /// <summary>
        /// True if this object was initialized and is ready for use.
        /// </summary>
        public bool IsInitilized { get; internal set; }

        /// <summary>
        /// A reference to the managed object handle that wraps the native V8 handle for this managed object.
        /// The default implementation for 'V8NativeObject' is to return itself, since it inherits from 'Handle'.
        /// Setting this property will call the inherited 'Set()' method to replace the handle associated with this object instance (this should never be done on
        /// objects created from templates ('V8ManagedObject' objects), otherwise callbacks from JavaScript to the managed side will not act as expected, if at all).
        /// </summary>
        public ObjectHandle Handle
        {
            get { return HandleInternal; }
            set
            {
                var handle = value != null ? (InternalHandle)value : InternalHandle.Empty;

                if (handle.ObjectId >= 0 && handle.Object != this)
                    throw new InvalidOperationException("Another managed object is already bound to this handle.");

                if (!HandleInternal.IsEmpty && HandleInternal.ObjectId >= 0)
                    throw new InvalidOperationException("Cannot replace a the handle of a V8Engine create object once it has been set."); // (IDs < 0 are not tracked in the V8.NET's object list)
                
                if (!handle.IsEmpty && !handle.IsObjectType)
                    throw new InvalidCastException(string.Format(ValueNotAnObjectErrorMessage, handle));

                HandleInternal.Set((Handle)value);
                Id = HandleInternal.ObjectId;
            }
        }
        internal ObjectHandle HandleInternal = ObjectHandle.Empty;

#if !(V1_1 || V2 || V3 || V3_5)
        /// <summary>
        /// Returns a "dynamic" reference to this object (which is simply the handle instance, which has dynamic support).
        /// </summary>
        public virtual dynamic AsDynamic
        {
            get { return HandleInternal; }
        }
#endif

        /// <summary>
        /// The prototype of the object (every JavaScript object implicitly has a prototype).
        /// </summary>
        public ObjectHandle Prototype
        {
            get
            {
                if (PrototypeInternal == null && HandleInternal.IsObjectType)
                {
                    // ... the prototype is not yet set, so get the prototype and wrap it ...
                    PrototypeInternal = HandleInternal.Prototype;
                }

                return PrototypeInternal;
            }
        }
        internal ObjectHandle PrototypeInternal;

        /// <summary>
        /// Returns true if this object is ready to be garbage collected by the native side.
        /// </summary>
        public bool IsManagedObjectWeak
        {
            get
            {
                using (Engine.ObjectsLockerInternal.ReadLock())
                {
                    return IdInternal == null || Engine.ObjectsInternal[IdInternal.Value].IsGCReady;
                }
            }
        }

        /// <summary>
        /// Used internally to quickly determine when an instance represents a binder object type, or static type binder function (faster than reflection!).
        /// </summary>
        public BindingMode BindingType { get { return BindingModeInternal; } }
        internal BindingMode BindingModeInternal;

        // --------------------------------------------------------------------------------------------------------------------

        public virtual InternalHandle this[string propertyName]
        {
            get
            {
                return HandleInternal.GetProperty(propertyName);
            }
            set
            {
                HandleInternal.SetProperty(propertyName, value);
            }
        }

        public virtual InternalHandle this[int index]
        {
            get
            {
                return HandleInternal.GetProperty(index);
            }
            set
            {
                HandleInternal.SetProperty(index, value);
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        public V8NativeObject()
        {
            ProxyInternal = this;
        }

        public V8NativeObject(IV8NativeObject proxy)
        {
            ProxyInternal = proxy ?? this;
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Called immediately after creating an object instance and setting the V8Engine property.
        /// Derived objects should override this for construction instead of using the constructor, and be sure to call back to this base method just before exiting (not at the beginning).
        /// In the constructor, the object only exists as an empty shell.
        /// It's ok to setup non-v8 values in constructors, but be careful not to trigger any calls into the V8Engine itself.
        /// <para>Note: Because this method is virtual, it does not guarantee that 'IsInitialized' will be considered.  Implementations should check against
        /// the 'IsInitilized' property.</para>
        /// </summary>
        public virtual ObjectHandle Initialize(bool isConstructCall, params InternalHandle[] args)
        {
            if (ProxyInternal != this && !IsInitilized)
                ProxyInternal.Initialize(this, isConstructCall, args);

            IsInitilized = true;

            return Handle;
        }

        /// <summary>
        /// (Exists only to support the 'IV8NativeInterface' interface and should not be called directly - call 'Initialize(isConstructCall, args)' instead.)
        /// </summary>
        public void Initialize(V8NativeObject owner, bool isConstructCall, params InternalHandle[] args)
        {
            if (!IsInitilized)
                Initialize(isConstructCall, args);
        }

        /// <summary>
        /// This is called on the GC finalizer thread to flag that this managed object entry can be collected.
        /// <para>Note: There are no longer any managed references to the object at this point; HOWEVER, there may still be NATIVE ones.
        /// This means the object may survive this process, at which point it's up to the worker thread to clean it up when the native V8 GC is ready.</para>
        /// </summary>
        ~V8NativeObject()
        {
            if (IdInternal != null && IdInternal.Value >= 0 && Engine != null) // (if there's no object ID, then let the instance die [skipping this block may allow the finalizer to complete])
            {
                var weakRef = Engine._GetObjectWeakReference(IdInternal.Value);
                weakRef.DoFinalize(this); // (will re-register this object for finalization)
                // (the native GC has not reported we can remove this yet, so just flag the collection attempt [note: if 'weakRef.CanCollect' is true here, then finalize will succeed])
                // (WARNING: 'lock (Engine._Objects){}' can conflict with the main thread if care is not taken)
                // (Past issue: When '{Engine}.GetObjects()' was called, and an '_Objects[?].Object' is accessed, a deadlock can occur since the finalizer has nulled all the weak references)
            }

            // ... check if the finalizer can reclaim this object, otherwise it needs to be placed in a queue for the worker, which will attempt to perform 
            // necessary tasks that shouldn't execute in a finalizer (such as trying to dispose of the native handle first before the object gets destroyed) ...
            // (note: 'CanFinalize' should return true if the object has no ties to V8.NET's object list, or association with template objects)
            if (!((IFinalizable)this).CanFinalize && Engine != null)
                lock (Engine.ObjectsToFinalizeInternal) // (this lock is sufficient, since only the worker accesses it, and the worker only locks when getting a reference from the array [and nothing more])
                {
                    EngineInternal.ObjectsToFinalizeInternal.Add(this); // (defer the finalize event to the worker thread instead)
                    GC.ReRegisterForFinalize(this);
                }
        }

        bool IFinalizable.CanFinalize { get { return (IdInternal == null || IdInternal.Value < 0) && m_template == null; } set { } }

        void IFinalizable.DoFinalize()
        {
            if (HandleInternal.IsWeakHandle) // (a handle is weak when there is only one reference [itself], which means this object is ready for the worker)
                _TryDisposeNativeHandle();
        }

        /// <summary>
        /// Called when there are no more references (on either the managed or native side) and the object is ready to be deleted from the V8.NET system.
        /// You should never call this from code directly unless you need to force the release of native resources associated with a custom implementation
        /// (and if so, a custom internal flag should be kept indicating whether or not the resources have been disposed).
        /// You should always override/implement this if you need to dispose of any native resources in custom implementations.
        /// DO NOT rely on the destructor (finalizer) - the object can still survive it.
        /// <para>Note: This can be triggered either by the worker thread, or on the call-back from the V8 garbage collector.  In either case, tread it as if
        /// it was called from the GC finalizer (not on the main thread).</para>
        /// *** If overriding, DON'T call back to this method, otherwise it will call back and end up in a cyclical call (and a stack overflow!). ***
        /// </summary>
        public virtual void Dispose()
        {
            if (ProxyInternal != this) ProxyInternal.Dispose();
        }

        /// <summary>
        /// This is called automatically when both the handle AND reference for the managed object are weak [no longer in use], in which case this object
        /// info instance is ready to be removed.
        /// </summary>
        internal void _TryDisposeNativeHandle()
        {
            if (!IsManagedObjectWeak ||
                (!HandleInternal.IsEmpty && (!HandleInternal.IsWeakHandle || HandleInternal.IsInPendingDisposalQueue)))
                return;

            HandleInternal.IsInPendingDisposalQueue = true; // (this also helps to make sure this method isn't called again)

            lock (EngineInternal.WeakObjectsInternal)
            {
                if (IdInternal == null)
                    throw new InvalidOperationException("IdInternal was not expected to be null.");

                EngineInternal.WeakObjectsInternal.Add(IdInternal.Value); // (queue on the worker to set a native weak reference to dispose of this object later when the native GC is ready)
            }
        }

        /// <summary>
        /// Called by the worker thread to make the native handle weak.  Once the native GC attempts to collect the underlying native object, then
        /// '_OnNativeGCRequested()' will get called to finalize the disposal of the managed object.
        /// </summary>
        internal void _MakeWeak()
        {
            V8NetProxy.MakeWeakHandle(HandleInternal);
            // (once the native V8 engine agrees, this instance will be removed due to a global GC callback registered when the engine was created)
        }

        internal bool _OnNativeGCRequested() // WARNING: The worker thread may trigger a V8 GC callback in its own thread!
        {
            using (Engine.ObjectsLockerInternal.WriteLock())
            {
                var functionTemplate = Template as FunctionTemplate;
                if (functionTemplate != null)
                    functionTemplate._RemoveFunctionType(Id);// (make sure to remove the function references from the template instance)

                Dispose(); // (notify any custom dispose methods to clean up)

                GC.SuppressFinalize(this); // (required otherwise the object's finalizer will be triggered again)

                HandleInternal.Dispose(); // (note: this may already be disposed, in which case this call does nothing)

                if (IdInternal == null)
                    throw new InvalidOperationException("IdInternal was not expected to be null.");

                Engine._ClearAccessors(IdInternal.Value); // (just to be sure - accessors are no longer needed once the native handle is GC'd)

                if (IdInternal != null)
                    EngineInternal._RemoveObjectWeakReference(IdInternal.Value);

                HandleInternal.ObjectId = -1;

                Template = null; // (note: this decrements a template counter; allows the GC finalizer to collect the object)
                IdInternal = null; // (also allows the GC finalizer to collect the object)
            }

            return true; // ("true" means to "continue disposal of native handle" [if not already empty])
        }

        // --------------------------------------------------------------------------------------------------------------------

        public static implicit operator InternalHandle(V8NativeObject obj) { return obj != null ? obj.HandleInternal.HandleInternal : InternalHandle.Empty; }
        public static implicit operator Handle(V8NativeObject obj) { return obj != null ? obj.HandleInternal : ObjectHandle.Empty; }
        public static implicit operator ObjectHandle(V8NativeObject obj) { return obj != null ? obj.HandleInternal : ObjectHandle.Empty; }

        // --------------------------------------------------------------------------------------------------------------------

        public override string ToString()
        {
            var objText = ProxyInternal.GetType().Name;
            var disposeText = HandleInternal.IsDisposed ? "Yes" : "No";
            return objText + " (ID: " + Id + " / Value: '" + HandleInternal + "' / Is Disposed?: " + disposeText + ")";
        }

        // --------------------------------------------------------------------------------------------------------------------

#if !(V1_1 || V2 || V3 || V3_5)
        public DynamicMetaObject GetMetaObject(Expression parameter)
        {
            return new DynamicHandle(this, parameter);
        }
#endif

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls the V8 'Set()' function on the underlying native object.
        /// Returns true if successful.
        /// </summary>
        /// <param name="attributes">Flags that describe the property behavior.  They must be 'OR'd together as needed.</param>
        public virtual bool SetProperty(string name, InternalHandle value)
        {
            return SetProperty(name, value, V8PropertyAttributes.None);
        }

        /// <summary>
        /// Calls the V8 'Set()' function on the underlying native object.
        /// Returns true if successful.
        /// </summary>
        /// <param name="attributes">Flags that describe the property behavior.  They must be 'OR'd together as needed.</param>
        public virtual bool SetProperty(string name, InternalHandle value, V8PropertyAttributes attributes)
        {
            return HandleInternal.HandleInternal.SetProperty(name, value, attributes);
        }

        /// <summary>
        /// Calls the V8 'Set()' function on the underlying native object.
        /// Returns true if successful.
        /// </summary>
        public virtual bool SetProperty(Int32 index, InternalHandle value)
        {
            return HandleInternal.HandleInternal.SetProperty(index, value);
        }

        /// <summary>
        /// Sets a property to a given object. If the object is not V8.NET related, then the system will attempt to bind the instance and all public members to
        /// the specified property name.
        /// Returns true if successful.
        /// </summary>
        /// <param name="name">The property name.</param>
        /// <param name="obj">Some value or object instance. 'Engine.CreateValue()' will be used to convert value types.</param>
        public virtual bool SetProperty(string name, object obj)
        {
            return SetProperty(name, obj, null, null, null);
        }

        /// <summary>
        /// Sets a property to a given object. If the object is not V8.NET related, then the system will attempt to bind the instance and all public members to
        /// the specified property name.
        /// Returns true if successful.
        /// </summary>
        /// <param name="name">The property name.</param>
        /// <param name="obj">Some value or object instance. 'Engine.CreateValue()' will be used to convert value types.</param>
        /// <param name="className">A custom in-script function name for the specified object type, or 'null' to use either the type name as is (the default) or any existing 'ScriptObject' attribute name.</param>
        /// <param name="recursive">For object instances, if true, then object reference members are included, otherwise only the object itself is bound and returned.
        /// For security reasons, public members that point to object instances will be ignored. This must be true to included those as well, effectively allowing
        /// in-script traversal of the object reference tree (so make sure this doesn't expose sensitive methods/properties/fields).</param>
        /// <param name="memberSecurity">For object instances, these are default flags that describe JavaScript properties for all object instance members that
        /// don't have any 'ScriptMember' attribute.  The flags should be 'OR'd together as needed.</param>
        public virtual bool SetProperty(string name, object obj, string className, bool? recursive, ScriptMemberSecurity? memberSecurity)
        {
            return HandleInternal.HandleInternal.SetProperty(name, obj, className, recursive, memberSecurity);
        }

        /// <summary>
        /// Binds a 'V8Function' object to the specified type and associates the type name (or custom script name) with the underlying object.
        /// Returns true if successful.
        /// </summary>
        /// <param name="type">The type to wrap.</param>
        /// <param name="propertyAttributes">Flags that describe the property behavior.  They must be 'OR'd together as needed.</param>
        /// <param name="className">A custom in-script function name for the specified type, or 'null' to use either the type name as is (the default) or any existing 'ScriptObject' attribute name.</param>
        /// <param name="recursive">For object types, if true, then object reference members are included, otherwise only the object itself is bound and returned.
        /// For security reasons, public members that point to object instances will be ignored. This must be true to included those as well, effectively allowing
        /// in-script traversal of the object reference tree (so make sure this doesn't expose sensitive methods/properties/fields).</param>
        /// <param name="memberSecurity">For object instances, these are default flags that describe JavaScript properties for all object instance members that
        /// don't have any 'ScriptMember' attribute.  The flags should be 'OR'd together as needed.</param>
        public virtual bool SetProperty(Type type)
        {
            return SetProperty(type, V8PropertyAttributes.None, null, null, null);
        }

        /// <summary>
        /// Binds a 'V8Function' object to the specified type and associates the type name (or custom script name) with the underlying object.
        /// Returns true if successful.
        /// </summary>
        /// <param name="type">The type to wrap.</param>
        /// <param name="propertyAttributes">Flags that describe the property behavior.  They must be 'OR'd together as needed.</param>
        /// <param name="className">A custom in-script function name for the specified type, or 'null' to use either the type name as is (the default) or any existing 'ScriptObject' attribute name.</param>
        /// <param name="recursive">For object types, if true, then object reference members are included, otherwise only the object itself is bound and returned.
        /// For security reasons, public members that point to object instances will be ignored. This must be true to included those as well, effectively allowing
        /// in-script traversal of the object reference tree (so make sure this doesn't expose sensitive methods/properties/fields).</param>
        /// <param name="memberSecurity">For object instances, these are default flags that describe JavaScript properties for all object instance members that
        /// don't have any 'ScriptMember' attribute.  The flags should be 'OR'd together as needed.</param>
        public virtual bool SetProperty(Type type, V8PropertyAttributes propertyAttributes, string className, bool? recursive, ScriptMemberSecurity? memberSecurity)
        {
            return HandleInternal.HandleInternal.SetProperty(type, propertyAttributes, className, recursive, memberSecurity);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls the V8 'Get()' function on the underlying native object.
        /// If the property doesn't exist, the 'IsUndefined' property will be true.
        /// </summary>
        public virtual InternalHandle GetProperty(string name)
        {
            return HandleInternal.HandleInternal.GetProperty(name);
        }

        /// <summary>
        /// Calls the V8 'Get()' function on the underlying native object.
        /// If the property doesn't exist, the 'IsUndefined' property will be true.
        /// </summary>
        public virtual InternalHandle GetProperty(Int32 index)
        {
            return HandleInternal.HandleInternal.GetProperty(index);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls the V8 'Delete()' function on the underlying native object.
        /// Returns true if the property was deleted.
        /// </summary>
        public virtual bool DeleteProperty(string name)
        {
            return HandleInternal.HandleInternal.GetProperty(name);
        }

        /// <summary>
        /// Calls the V8 'Delete()' function on the underlying native object.
        /// Returns true if the property was deleted.
        /// </summary>
        public virtual bool DeleteProperty(Int32 index)
        {
            return HandleInternal.HandleInternal.GetProperty(index);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls the V8 'SetAccessor()' function on the underlying native object to create a property that is controlled by "getter" and "setter" callbacks.
        /// </summary>
        public virtual void SetAccessor(string name,
            V8NativeObjectPropertyGetter getter, V8NativeObjectPropertySetter setter)
        {
            SetAccessor(name, getter, setter, V8PropertyAttributes.None, V8AccessControl.Default);
        }

        /// <summary>
        /// Calls the V8 'SetAccessor()' function on the underlying native object to create a property that is controlled by "getter" and "setter" callbacks.
        /// </summary>
        public virtual void SetAccessor(string name,
            V8NativeObjectPropertyGetter getter, V8NativeObjectPropertySetter setter, V8PropertyAttributes attributes)
        {
            SetAccessor(name, getter, setter, attributes, V8AccessControl.Default);
        }

        /// <summary>
        /// Calls the V8 'SetAccessor()' function on the underlying native object to create a property that is controlled by "getter" and "setter" callbacks.
        /// </summary>
        public virtual void SetAccessor(string name,
            V8NativeObjectPropertyGetter getter, V8NativeObjectPropertySetter setter,
            V8PropertyAttributes attributes, V8AccessControl access)
        {
            HandleInternal.HandleInternal.SetAccessor(name, getter, setter, attributes, access);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns a list of all property names for this object (including all objects in the prototype chain).
        /// </summary>
        public virtual string[] GetPropertyNames()
        {
            return HandleInternal.HandleInternal.GetPropertyNames();
        }

        /// <summary>
        /// Returns a list of all property names for this object (excluding the prototype chain).
        /// </summary>
        public virtual string[] GetOwnPropertyNames()
        {
            return HandleInternal.HandleInternal.GetOwnPropertyNames();
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Get the attribute flags for a property of this object.
        /// If a property doesn't exist, then 'V8PropertyAttributes.None' is returned
        /// (Note: only V8 returns 'None'. The value 'Undefined' has an internal proxy meaning for property interception).</para>
        /// </summary>
        public virtual V8PropertyAttributes GetPropertyAttributes(string name)
        {
            return HandleInternal.HandleInternal.GetPropertyAttributes(name);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls an object property with a given name on a specified object as a function and returns the result.
        /// The '_this' property is the "this" object within the function when called.
        /// </summary>
        public virtual InternalHandle Call(string functionName, InternalHandle _this, params InternalHandle[] args)
        {
            return HandleInternal.HandleInternal.Call(functionName, _this, args);
        }

        /// <summary>
        /// Calls an object property with a given name on a specified object as a function and returns the result.
        /// </summary>
        public virtual InternalHandle StaticCall(string functionName, params InternalHandle[] args)
        {
            return HandleInternal.HandleInternal.StaticCall(functionName, args);
        }

        /// <summary>
        /// Calls the underlying object as a function.
        /// The '_this' parameter is the "this" reference within the function when called.
        /// </summary>
        public virtual InternalHandle Call(InternalHandle _this, params InternalHandle[] args)
        {
            return HandleInternal.HandleInternal.Call(_this, args);
        }

        /// <summary>
        /// Calls the underlying object as a function.
        /// The 'this' property will not be specified, which will default to the global scope as expected.
        /// </summary>
        public virtual InternalHandle StaticCall(params InternalHandle[] args)
        {
            return HandleInternal.HandleInternal.StaticCall(args);
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================

    /// <summary>
    /// This generic version of 'V8NativeObject' allows injecting your own class by implementing the 'IV8NativeObject' interface.
    /// </summary>
    /// <typeparam name="T">Your own class, which implements the 'IV8NativeObject' interface.  Don't use the generic version if you are able to inherit from 'V8NativeObject' instead.</typeparam>
    public class V8NativeObject<T> : V8NativeObject
        where T : IV8NativeObject, new()
    {
        // --------------------------------------------------------------------------------------------------------------------

        public V8NativeObject()
            : base(new T())
        {
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================
}
