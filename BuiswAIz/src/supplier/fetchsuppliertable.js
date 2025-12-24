// supplier/fetchsuppliertable.js
import { supabase } from "../supabase";

// Fetch suppliers only for the current business
export async function fetchSupplier(businessId) {
  if (!businessId) return [];

  const { data, error } = await supabase
    .from("suppliers")
    .select("*")
    .eq("businessid", businessId)   // 🔹 filter by business
    .order("supplierid", { ascending: true });

  if (error) {
    console.error("Fetch error:", error);
    throw error;
  }

  return data;
}

// Fetch suppliers with product counts for the current business
export async function fetchSupplierWithProducts(businessId) {
  if (!businessId) return [];

  const { data, error } = await supabase
    .from("supplier_with_products")
    .select("*")
    .eq("businessid", businessId)  // 🔹 filter by business
    .order("totalproducts", { ascending: false });

  if (error) {
    console.error("Fetch error (with products):", error);
    throw error;
  }

  return data;
}
