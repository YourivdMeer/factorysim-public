namespace FactorySim.Core;

/// <summary>What kind of thing an item from the library is, decided by its id.</summary>
public static class ItemKinds
{
    /// <summary>
    /// An item is metal when its library id ends in "_metal" (<c>raw_material_metal</c>,
    /// <c>product_base_metal</c>, ...). An inductive sensor only sees these.
    /// </summary>
    public static bool IsMetal(string? itemId) => itemId is not null && itemId.EndsWith("_metal", StringComparison.Ordinal);

    /// <summary>
    /// The number a vision sensor reports for each kind of item; 0 is nothing (or an item that is not in
    /// the table). The table is fixed, so a PLC program keeps working when the library grows: new items
    /// get the next free number, existing numbers never change.
    /// </summary>
    public static readonly IReadOnlyList<string> NumberedItems = new[]
    {
        "box_low", "box_high", "box_small", "box_medium", "box_large",                                  // 1-5
        "pallet", "pallet_square", "stackable_box",                                                     // 6-8
        "raw_material_blue", "product_base_blue", "product_lid_blue", "final_product_blue",             // 9-12
        "raw_material_green", "product_base_green", "product_lid_green", "final_product_green",         // 13-16
        "raw_material_metal", "product_base_metal", "product_lid_metal", "final_product_metal",         // 17-20
    };

    /// <summary>The number of a kind of item for a vision sensor: 1 to 20, or 0 for none.</summary>
    public static int Number(string? itemId)
    {
        if (itemId is null)
        {
            return 0;
        }

        for (int i = 0; i < NumberedItems.Count; i++)
        {
            if (NumberedItems[i] == itemId)
            {
                return i + 1;
            }
        }

        return 0;
    }
}
