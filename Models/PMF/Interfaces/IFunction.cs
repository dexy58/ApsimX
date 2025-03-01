namespace Models.Functions
{
    using Models.Core;

    /// <summary>Interface for a function</summary>
    [ValidParent(DropAnywhere = true)]
    public interface IFunction : IModel
    {
        /// <summary>Gets the value of the function.</summary>
        double Value(int arrayIndex = -1);
    }

    /// <summary>Interface for a function</summary>
    public interface IIndexedFunction : IModel
    {
        /// <summary>Gets the value of the function.</summary>
        double ValueIndexed(double dX);
    }
}