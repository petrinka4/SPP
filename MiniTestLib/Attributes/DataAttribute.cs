using System;

namespace MiniTestLib.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class DataAttribute : Attribute
    {
        public object[] Values { get; }
        public DataAttribute(params object[] values) => Values = values;
    }
}
