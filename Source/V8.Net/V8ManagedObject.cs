namespace V8.Net
{
    using System;
    using System.Collections.Generic;

#if V2 || V3 || V3_5
#else
    using System.Dynamic;
    using System.Linq.Expressions;
#endif

    // ========================================================================================================================

    /// <summary>
    /// An interface for objects wrapped by V8NativeObject instances.
    /// <para>These methods are called in proxy to the V8NativeObject's related methods ('Initialize(...)' and 'Dispose(...)').</para>
    /// The arguments passed to 'Initialize(...)' ('isConstructCall' and 'args') are the responsibility of the developer - except for the binder, which will
    /// pass in the values as expected.
    /// </summary>
    public interface IV8NativeObject : IDisposable
    {
        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Called immediately after creating an object instance and setting the V8Engine property.
        /// Derived objects should override this for construction instead of using the constructor, and be sure to call back to this base method just before exiting (not at the beginning).
        /// In the constructor, the object only exists as an empty shell.
        /// It's ok to setup non-v8 values in constructors, but be careful not to trigger any calls into the V8Engine itself.
        /// <para>Note: Because this method is virtual, it does not guarantee that 'IsInitialized' will be considered.  Implementations should check against
        /// the 'IsInitilized' property.</para>
        /// </summary>
        void Initialize(V8NativeObject owner, bool isConstructCall, params InternalHandle[] args);

        // --------------------------------------------------------------------------------------------------------------------

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
        new void Dispose();

        // --------------------------------------------------------------------------------------------------------------------
    }

    /// <summary>
    /// The 'V8ManagedObject' class implements 'DynamicObject' for you, but if dynamic objects are not required, feel free to implement
    /// the 'IV8ManagedObject' interface for your own classes instead.
    /// </summary>
    public interface IV8ManagedObject : IV8NativeObject
    {
        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Holds a Key->Value reference to all property names and values for the JavaScript object that this managed object represents.
        /// Accessing the 'Properties' property without setting it first creates a new dictionary object by default.
        /// </summary>
        IDictionary<string, IJSProperty> Properties { get; }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Intercepts JavaScript access for properties on the associated JavaScript object for retrieving a value.
        /// <para>To allow the V8 engine to perform the default get action, return "Handle.Empty".</para>
        /// </summary>
        InternalHandle NamedPropertyGetter(ref string propertyName);

        /// <summary>
        /// Intercepts JavaScript access for properties on the associated JavaScript object for setting values.
        /// <para>To allow the V8 engine to perform the default set action, return "Handle.Empty".</para>
        /// </summary>
        InternalHandle NamedPropertySetter(ref string propertyName, InternalHandle value, V8PropertyAttributes attributes);

        /// <summary>
        /// Let's the V8 engine know the attributes for the specified property.
        /// <para>To allow the V8 engine to perform the default get action, return "null".</para>
        /// </summary>
        V8PropertyAttributes? NamedPropertyQuery(ref string propertyName);

        /// <summary>
        /// Intercepts JavaScript request to delete a property.
        /// <para>To allow the V8 engine to perform the default get action, return "null".</para>
        /// </summary>
        bool? NamedPropertyDeleter(ref string propertyName);

        /// <summary>
        /// Returns the results of enumeration (such as when "for..in" is used).
        /// <para>To allow the V8 engine to perform the default set action, return "Handle.Empty".</para>
        /// </summary>
        InternalHandle NamedPropertyEnumerator();

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Intercepts JavaScript access for properties on the associated JavaScript object for retrieving a value.
        /// <para>To allow the V8 engine to perform the default get action, return "Handle.Empty".</para>
        /// </summary>
        InternalHandle IndexedPropertyGetter(int index);

        /// <summary>
        /// Intercepts JavaScript access for properties on the associated JavaScript object for setting values.
        /// <para>To allow the V8 engine to perform the default set action, return "Handle.Empty".</para>
        /// </summary>
        InternalHandle IndexedPropertySetter(int index, InternalHandle value);

        /// <summary>
        /// Let's the V8 engine know the attributes for the specified property.
        /// <para>To allow the V8 engine to perform the default get action, return "null".</para>
        /// </summary>
        V8PropertyAttributes? IndexedPropertyQuery(int index);

        /// <summary>
        /// Intercepts JavaScript request to delete a property.
        /// <para>To allow the V8 engine to perform the default get action, return "null".</para>
        /// </summary>
        bool? IndexedPropertyDeleter(int index);

        /// <summary>
        /// Returns the results of enumeration (such as when "for..in" is used).
        /// <para>To allow the V8 engine to perform the default set action, return "Handle.Empty".</para>
        /// </summary>
        InternalHandle IndexedPropertyEnumerator();

        // --------------------------------------------------------------------------------------------------------------------
    }

    /// <summary>
    /// Represents a C# (managed) JavaScript object.  Properties are set on the object within the class itself, and not within V8.
    /// This is done by using V8 object interceptors (callbacks).  By default, this object is used for the global environment.
    /// <para>The inherited 'V8NativeObject' base class implements 'DynamicObject' for you, but if dynamic objects are not required, 
    /// feel free to implement the 'IV8ManagedObject' interface for your own classes instead; however, you also will have to call the
    /// V8NetProxy static methods yourself if you need functionality supplied by V8NativeObject.</para>
    /// <para>Note: It's faster to work with the properties on the managed side using this object, but if a lot of properties won't be changing,
    /// it may be faster to access properties within V8 itself.  To do so, simply create a basic V8NativeObject using 'V8Engine.CreateObject()'
    /// instead.</para>
    /// </summary>
    public class V8ManagedObject : V8NativeObject, IV8ManagedObject
    {
        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A reference to the managed object handle that wraps the native V8 handle for this managed object (this simply returns 'base.Handle'). 
        /// You should never change handles on managed objects because they are usually associated with object interceptors,
        /// and changing the handle will break the call-back system.
        /// </summary>
        new Handle Handle { get { return _Handle; } }

        /// <summary>
        /// A reference to the ObjectTemplate instance that owns this object.
        /// </summary>
        public ObjectTemplate ObjectTemplate { get { return (ObjectTemplate)Template; } }

        /// <summary>
        /// Holds a Key->Value reference to all property names and values for the JavaScript object that this managed object represents.
        /// Accessing the 'Properties' property without setting it first creates a new dictionary object by default.
        /// </summary>
        public virtual IDictionary<string, IJSProperty> Properties
        {
            get { return _Properties ?? (_Properties = new Dictionary<string, IJSProperty>()); }
            protected set { _Properties = value; }
        }
        protected IDictionary<string, IJSProperty> _Properties;

        // --------------------------------------------------------------------------------------------------------------------

        new public IJSProperty this[string propertyName]
        {
            get
            {
                IJSProperty value;
                if (Properties.TryGetValue(propertyName, out value))
                    return value;
                return JSProperty.Empty;
            }
            set
            {
                Properties[propertyName] = value;
            }
        }

        new public IJSProperty this[int index]
        {
            get
            {
                IJSProperty value;
                if (Properties.TryGetValue(index.ToString(), out value))
                    return value;
                return JSProperty.Empty;
            }
            set
            {
                Properties[index.ToString()] = value;
            }
        }

        //??public override InternalHandle this[string propertyName]
        //{
        //    get
        //    {
        //        return NamedPropertyGetter(ref propertyName);
        //    }
        //    set
        //    {
        //        NamedPropertySetter(ref propertyName, value);
        //    }
        //}

        //??public override InternalHandle this[int index]
        //{
        //    get
        //    {
        //        return IndexedPropertyGetter(index);
        //    }
        //    set
        //    {
        //        IndexedPropertySetter(index, value);
        //    }
        //}

        // --------------------------------------------------------------------------------------------------------------------

        public V8ManagedObject()
            : base()
        {
        }

        public V8ManagedObject(IV8ManagedObject proxy)
            : base(proxy)
        {
        }

        // --------------------------------------------------------------------------------------------------------------------

        public virtual InternalHandle NamedPropertyGetter(ref string propertyName)
        {
            if (_Proxy != this)
            {
                var result = ((IV8ManagedObject)_Proxy).NamedPropertyGetter(ref propertyName);
                if (!result.IsUndefined) return result;
            }

            return this[propertyName].Value;
        }

        public virtual InternalHandle NamedPropertySetter(ref string propertyName, InternalHandle value)
        {
            return NamedPropertySetter(ref propertyName, value, V8PropertyAttributes.Undefined);
        }

        public virtual InternalHandle NamedPropertySetter(ref string propertyName, InternalHandle value, V8PropertyAttributes attributes)
        {
            if (_Proxy != this)
            {
                var result = ((IV8ManagedObject)_Proxy).NamedPropertySetter(ref propertyName, value, attributes);
                if (!result.IsUndefined) return result;
            }

            var jsVal = this[propertyName];

            if (jsVal.Value.IsEmpty)
                this[propertyName] = jsVal = new JSProperty(value, attributes != V8PropertyAttributes.Undefined ? attributes : V8PropertyAttributes.None);
            else
            {
                if (attributes != V8PropertyAttributes.Undefined)
                {
                    jsVal.Attributes = attributes;
                    jsVal.Value = value; // (note: updating attributes automatically assumes writable access)
                }
                else if ((jsVal.Attributes & V8PropertyAttributes.ReadOnly) == 0)
                    jsVal.Value = value;
            }

            return jsVal.Value;
        }

        public virtual V8PropertyAttributes? NamedPropertyQuery(ref string propertyName)
        {
            if (_Proxy != this)
            {
                var result = ((IV8ManagedObject)_Proxy).NamedPropertyQuery(ref propertyName);
                if (result != null) return result;
            }

            return this[propertyName].Attributes;
        }

        public virtual bool? NamedPropertyDeleter(ref string propertyName)
        {
            if (_Proxy != this)
            {
                var result = ((IV8ManagedObject)_Proxy).NamedPropertyDeleter(ref propertyName);
                if (result != null) return result;
            }

            var jsVal = this[propertyName];

            if ((jsVal.Attributes & V8PropertyAttributes.DontDelete) != 0)
                return false;

            return Properties.Remove(propertyName);
        }

        public virtual InternalHandle NamedPropertyEnumerator()
        {
            if (_Proxy != this)
            {
                var result = ((IV8ManagedObject)_Proxy).NamedPropertyEnumerator();
                if (!result.IsUndefined) return result;
            }

            List<string> names = new List<string>(Properties.Count);

            foreach (var prop in Properties)
                if (prop.Value != null && (prop.Value.Attributes & V8PropertyAttributes.DontEnum) == 0)
                    names.Add(prop.Key);

            return Engine.CreateValue(names);
        }

        // --------------------------------------------------------------------------------------------------------------------

        public virtual InternalHandle IndexedPropertyGetter(int index)
        {
            var propertyName = index.ToString();
            return NamedPropertyGetter(ref propertyName);
        }

        public virtual InternalHandle IndexedPropertySetter(int index, InternalHandle value)
        {
            var propertyName = index.ToString();
            return NamedPropertySetter(ref propertyName, value);
        }

        public virtual V8PropertyAttributes? IndexedPropertyQuery(int index)
        {
            var propertyName = index.ToString();
            return NamedPropertyQuery(ref propertyName);
        }

        public virtual bool? IndexedPropertyDeleter(int index)
        {
            var propertyName = index.ToString();
            return NamedPropertyDeleter(ref propertyName);
        }

        public virtual InternalHandle IndexedPropertyEnumerator()
        {
            return NamedPropertyEnumerator();
        }

        // --------------------------------------------------------------------------------------------------------------------
        // Since some base methods operate on object properties, and the properties exist on this managed object, we override
        // them here to speed things up.

        public override bool SetProperty(string name, InternalHandle value)
        {
            return SetProperty(name, value, V8PropertyAttributes.None);
        }

        public override bool SetProperty(string name, InternalHandle value, V8PropertyAttributes attributes)
        {
            var result = NamedPropertySetter(ref name, value, attributes);
            return !result.IsUndefined || base.SetProperty(name, value, attributes);
        }

        public override bool SetProperty(int index, InternalHandle value)
        {
            var result = IndexedPropertySetter(index, value);
            if (result.IsUndefined) return base.SetProperty(index, value);
            return true;
        }

        public override InternalHandle GetProperty(string name)
        {
            var result = NamedPropertyGetter(ref name);
            if (result.IsUndefined) return base.GetProperty(name);
            return result;
        }

        public override InternalHandle GetProperty(int index)
        {
            var result = IndexedPropertyGetter(index);
            if (result.IsUndefined) return base.GetProperty(index);
            return result;
        }

        public override bool DeleteProperty(string name)
        {
            var result = NamedPropertyDeleter(ref name);
            if (result == null) return base.DeleteProperty(name);
            return result ?? false;
        }

        public override bool DeleteProperty(int index)
        {
            var result = IndexedPropertyDeleter(index);
            if (result == null) return base.DeleteProperty(index);
            return result ?? false;
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================

    /// <summary>
    /// This generic version of 'V8ManagedObject' allows injecting your own class by implementing the 'IV8ManagedObject' interface.
    /// </summary>
    /// <typeparam name="T">Your own class, which implements the 'IV8ManagedObject' interface.  Don't use the generic version if you are able to inherit from 'V8ManagedObject' instead.</typeparam>
    public unsafe class V8ManagedObject<T> : V8ManagedObject
        where T : IV8ManagedObject, new()
    {
        // --------------------------------------------------------------------------------------------------------------------

        public V8ManagedObject()
            : base(new T())
        {
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================
}
