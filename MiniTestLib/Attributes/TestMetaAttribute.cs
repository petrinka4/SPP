using System;

namespace MiniTestLib.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class TestMetaAttribute : Attribute
    {
        public string Category { get; }
        public int Priority { get; }
        public string Author { get; }

        public TestMetaAttribute(string category = "", int priority = 0, string author = "")
        {
            Category = category;
            Priority = priority;
            Author = author;
        }
    }
}