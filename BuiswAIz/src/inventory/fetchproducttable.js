  import { supabase } from "../supabase";

  export async function fetchProducts() {
    const { data, error } = await supabase
      .from("products")
      .select(`
        *,
        suppliers:supplierid (
          suppliername
        )
      `)
      .order("productid", { descending: false }); 

    if (error) {
      console.error("Fetch error:", error);
      throw error;
    }

    return data;
  }
