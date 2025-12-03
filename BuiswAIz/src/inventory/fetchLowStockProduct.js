import { supabase } from "../supabase";

export const fetchLowStockProducts = async () => {
  try {
    const { data, error } = await supabase.rpc("get_low_stock_categories");

    if (error) throw error;

    // Optional: sort by stock deficit
    const lowStock = data.sort(
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
