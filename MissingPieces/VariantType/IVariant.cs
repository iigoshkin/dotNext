using System.Dynamic;

namespace MissingPieces.VariantType
{
	/// <summary>
	/// A root interface for all variant data containers.
	/// </summary>
    public interface IVariant: IDynamicMetaObjectProvider, IOptional
    {
		/// <summary>
		/// Gets value stored in the container.
		/// </summary>
        object Value { get; }
    }
}