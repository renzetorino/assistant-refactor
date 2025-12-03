import { supabase } from "../supabase";

export async function fetchSupplier() {
  const { data, error } = await supabase
    .from("suppliers")
    .select(`*`)
    .order("supplierid", { descending: false }); 

  if (error) {
    console.error("Fetch error:", error);
    throw error;
  }

  return data;
}


export async function fetchSupplierWithProducts() {
  const { data, error } = await supabase
    .from("supplier_with_products")
    .select("*")
    .order("totalproducts", { ascending: false });  

  if (error) {
    console.error("Fetch error (with products):", error);
    throw error;
  }

  return data;
}