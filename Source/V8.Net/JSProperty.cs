namespace V8.Net
{
    using System;
    using System.Globalization;

    // ========================================================================================================================

    public interface IJSProperty
    {
        /// <summary>
        /// A JavaScript associated value.
        /// Call one of the "Create???()" methods to create/build a required type for the JavaScript value that represents 'Source'.
        /// <para>Note: Because this is a value type property, just assign a value to the property - DON'T call '{InternalHandle}.Set()', it will not work as expected.</para>
        /// </summary>
        InternalHandle Value { get; set; }

        /// <summary>
        /// 'V8PropertyAttributes' flags combined to describe the value, such as visibility, or what kind of access is allowed.
        /// </summary>
        V8PropertyAttributes Attributes { get; set; }
    }

    /// <summary>
    /// A convenient JavaScript property wrapper which also holds JavaScript property attribute flags. The generic type 'TSourceValue' is the type of value to be stored on the managed side.
    /// This JSProperty object also allows storing an associated V8 value handle. Having both a managed source value and a separate V8 value allows the source
    /// value to be represented in JavaScript as a different type. For example, a value may exist locally as a string, but in JavaScript as a number (or vice versa).
    /// Developers can inherit from this class if desired, or choose to go with a custom implementation using the IJSProperty interface instead.
    /// </summary>
    /// <typeparam name="TValueSource">When implementing properties for an IV8ManagedObject, this is the type that will store the property source value/details (such as 'object' - as already implemented in the derived 'JSProperty' class [the non-generic version]).</typeparam>
    public class JSProperty<TValueSource> : IJSProperty, IHandleBased, IFinalizable
    {
        /// <summary>
        /// This is a developer-defined source reference for the JavaScript 'Value' property if needed. It is not used by V8.Net.
        /// </summary>
        public TValueSource Source;

        /// <summary>
        /// A JavaScript associated value.  By default, this returns 'Handle.Empty' (which means 'Value' is 'null' internally).
        /// Call one of the "V8Engine.Create???()" methods to create/build a required type for the JavaScript value that represents 'Source'.
        /// <para>Note: Because this is a value type property, just assign a value to the property - DON'T call '{InternalHandle}.Set()', it will not work as expected.</para>
        /// </summary>
        InternalHandle IJSProperty.Value
        {
            get { return m_value; }
            set { m_value.Set(value); }
        }

#pragma warning disable 649
        private InternalHandle m_value;
#pragma warning restore 649

        /// <summary>
        /// 'V8PropertyAttributes' flags combined to describe the value, such as visibility, or what kind of access is allowed.
        /// </summary>
        V8PropertyAttributes IJSProperty.Attributes
        {
            get { return m_attributes; }
            set { m_attributes = value; }
        }

        private V8PropertyAttributes m_attributes;

        /// <summary>
        /// Create a new JSProperty instance to help keep track of JavaScript object properties on managed objects.
        /// </summary>
        public JSProperty() : this(V8PropertyAttributes.None)
        {
        }

        /// <summary>
        /// Create a new JSProperty instance to help keep track of JavaScript object properties on managed objects.
        /// </summary>
        public JSProperty(V8PropertyAttributes attributes)
        {
            Source = default(TValueSource);
            m_attributes = attributes;
        }

        public JSProperty(TValueSource source) : this(source, V8PropertyAttributes.None)
        {
        }

        public JSProperty(TValueSource source, V8PropertyAttributes attributes)
        {
            Source = source;
            m_attributes = attributes;
        }

        public JSProperty(TValueSource source, InternalHandle value) : this(source, value, V8PropertyAttributes.None)
        {
        }

        public JSProperty(TValueSource source, InternalHandle value,
            V8PropertyAttributes attributes) : this(source, attributes)
        {
            m_value.Set(value);
        }

        public JSProperty(InternalHandle value)
            : this(value, V8PropertyAttributes.None)
        {
        }

        public JSProperty(InternalHandle value, V8PropertyAttributes attributes)
            : this(default(TValueSource), value, attributes)
        {
        }

        public JSProperty(V8Engine engine, object value)
            : this(engine, value, V8PropertyAttributes.None)
        {
        }

        public JSProperty(V8Engine engine, object value, V8PropertyAttributes attributes)
            : this(InternalHandle.Empty, attributes)
        {
            m_value.Set(engine != null ? engine.CreateValue(value) : InternalHandle.Empty);
        }

        ~JSProperty()
        {
            if (!((IFinalizable)this).CanFinalize && m_value.Engine != null)
                lock (m_value.Engine.ObjectsToFinalizeInternal)
                {
                    m_value.Engine.ObjectsToFinalizeInternal.Add(this);
                    GC.ReRegisterForFinalize(this);
                }
        }

        bool IFinalizable.CanFinalize { get { return m_value.IsEmpty; } set { } }

        void IFinalizable.DoFinalize()
        {
            m_value.Dispose();
        }

        public static implicit operator InternalHandle(JSProperty<TValueSource> jsVal)
        { return jsVal.m_value; }

        public override string ToString()
        {
            return m_value.ToString(CultureInfo.CurrentCulture);
        }

        V8Engine IHandleBased.Engine { get { return m_value.Engine; } }
        Handle IHandleBased.AsHandle() { return m_value; }
        InternalHandle IHandleBased.AsInternalHandle { get { return m_value; } }
        V8NativeObject IHandleBased.Object { get { return m_value.Object; } }
    }

    /// <summary>
    /// A convenient 'MemberInfo' specific wrapper which holds JavaScript property value and attribute flags for managed object members.
    /// For custom implementations, see <see cref="JSProperty&lt;TValueSource>"/>.
    /// </summary>
    public class JSProperty : JSProperty<object>
    {
        private class EmptyJSProperty : IJSProperty
        {
            InternalHandle IJSProperty.Value { get { return InternalHandle.Empty; } set { throw new InvalidOperationException("This JSProperty instance represents a NULL state and cannot be set."); } }
            V8PropertyAttributes IJSProperty.Attributes { get { return V8PropertyAttributes.Undefined; } set { throw new InvalidOperationException("This JSProperty instance represents a NULL state and cannot have attributes."); } }
        }

        /// <summary>
        /// Represents an empty JSProperty, which is simply used to return an empty 'Value' property (as 'Handle.Empty').
        /// <para>The purpose is to prevent having to perform null reference checks when needing to reference the 'Value' property.</para>
        /// </summary>
        public readonly static IJSProperty Empty = new EmptyJSProperty();

        /// <summary>
        /// Create a new JSProperty instance to help keep track of JavaScript object properties on managed objects.
        /// </summary>
        public JSProperty() : this(V8PropertyAttributes.None) {
        }

        /// <summary>
        /// Create a new JSProperty instance to help keep track of JavaScript object properties on managed objects.
        /// </summary>
        public JSProperty(V8PropertyAttributes attributes) : base(attributes)
        {
        }

        public JSProperty(object source)
            : this(source, V8PropertyAttributes.None)
        {
        }

        public JSProperty(object source, V8PropertyAttributes attributes)
            : base(source, attributes)
        {
        }

        public JSProperty(object source, InternalHandle value) : this(source, value, V8PropertyAttributes.None)
        {
        }

        public JSProperty(object source, InternalHandle value,
            V8PropertyAttributes attributes) : base(source, value, attributes)
        {
        }

        public JSProperty(InternalHandle value)
            : this(value, V8PropertyAttributes.None)
        {
        }

        public JSProperty(InternalHandle value, V8PropertyAttributes attributes)
            : base(value, attributes)
        {
        }

        public JSProperty(V8Engine engine, object value)
            : this(engine, value, V8PropertyAttributes.None)
        {
        }

        public JSProperty(V8Engine engine, object value, V8PropertyAttributes attributes)
            : base(engine, value, attributes)
        {
        }

    }

    // ========================================================================================================================
}
