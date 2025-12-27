import { supabase } from "../supabase";

export async function fetchProducts(businessId) {
  const { data, error } = await supabase
    .from("products")
    .select(`
      *,
      suppliers:supplierid (
        suppliername
      )
    `)
    .eq("businessid", businessId) // 🔹 filter by business
    .order("productid", { descending: false });

  if (error) {
    console.error("Fetch error:", error);
    throw error;
  }

  return data;
}
