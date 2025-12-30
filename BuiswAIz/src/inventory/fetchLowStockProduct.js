import { supabase } from "../supabase";

/**
 * Fetch low stock product categories for a specific business
 * Only returns products BELOW reorder point
 * @param {number} businessId
 */
export const fetchLowStockProducts = async (businessId) => {
  try {
    const { data, error } = await supabase
      .from("productcategory")
      .select(`
        *,
        product:productid (
          productname,
          image_url,
          businessid
        )
      `)
      .eq("product.businessid", businessId);

    if (error) throw error;

    const lowStock = data
      .map((cat) => {
        if (!cat.product) return null;

        const current = Number(cat.currentstock);
        const reorder = Number(cat.reorderpoint);

        // 🔹 ONLY BELOW reorder point
        if (current >= reorder) return null;

        return {
          ...cat,
          productname: cat.product.productname,
          image_url: cat.product.image_url,
        };
      })
      .filter(Boolean)
      // 🔹 sort by how critical it is (lowest first)
      .sort(
        (a, b) =>
          (Number(a.currentstock) - Number(a.reorderpoint)) -
          (Number(b.currentstock) - Number(b.reorderpoint))
      );

    return lowStock;
  } catch (err) {
    console.error("Fetch error:", err);
    return [];
  }
};
