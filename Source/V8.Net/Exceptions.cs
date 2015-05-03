namespace V8.Net
{
    using System;

    // ========================================================================================================================

    public class V8Exception : Exception
    {
        InternalHandle Handle { get { return m_handle; } }
        InternalHandle m_handle;

        public V8Exception(InternalHandle handle, Exception innerException)
            : base(handle.AsString, innerException)
        {
            m_handle.Set(handle);
        }

        public V8Exception(InternalHandle handle)
            : base(handle.AsString)
        {
            m_handle.Set(handle);
        }

        ~V8Exception() { m_handle.Dispose(); }
    }

    public class V8InternalErrorException : V8Exception
    {
        public V8InternalErrorException(InternalHandle handle) : base(handle) { }
        public V8InternalErrorException(InternalHandle handle, Exception innerException) : base(handle) { }
    }

    public class V8CompilerErrorException : V8Exception
    {
        public V8CompilerErrorException(InternalHandle handle) : base(handle) { }
        public V8CompilerErrorException(InternalHandle handle, Exception innerException) : base(handle) { }
    }

    public class V8ExecutionErrorException : V8Exception
    {
        public V8ExecutionErrorException(InternalHandle handle) : base(handle) { }
        public V8ExecutionErrorException(InternalHandle handle, Exception innerException) : base(handle) { }
    }

    // ========================================================================================================================
}
