// IL2CPP reads this attribute by its name, and Unity's documents have each project declare it. Internal, so that a project with its own does not clash.
namespace Unity.IL2CPP.CompilerServices
{
    internal enum Option
    {
        NullChecks = 1,
        ArrayBoundsChecks = 2,
        DivideByZeroChecks = 3,
    }

    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Method | System.AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
    internal sealed class Il2CppSetOptionAttribute : System.Attribute
    {
        public Option Option { get; }
        public object Value { get; }

        public Il2CppSetOptionAttribute(Option option, object value)
        {
            Option = option;
            Value = value;
        }
    }
}
