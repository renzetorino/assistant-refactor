import { supabase } from "../supabase";

/* ===============================
   ACTIVE (NOT YET INHERITED)
================================ */
export const fetchActiveBatches = async (businessId) => {
  if (!businessId) throw new Error("businessId is required");

  const { data, error } = await supabase
    .from("restockstorage")
    .select(`
      restockid,
      batchCode,
      new_stock,
      new_cost,
      new_price,
      datereceived,
      dateInherited,
      products (
        productid,
        productname
      ),
      productcategory (
        productcategoryid,
        color,
        agesize,
        currentstock,
        reorderpoint
      ),
      suppliers (
        suppliername
      )
    `)
    .eq("businessid", businessId)   // ✅ FIXED
    .is("dateInherited", null)
    .order("created_at", { ascending: false });

  if (error) throw error;
  return data;
};


/* ===============================
   INHERITED BATCHES
================================ */
export const fetchInheritedBatches = async (businessId) => {
  if (!businessId) throw new Error("businessId is required");

  const { data, error } = await supabase
    .from("restockstorage")
    .select(`
      restockid,
      batchCode,
      new_stock,
      new_cost,
      new_price,
      datereceived,
      dateInherited,
      products (
        productid,
        productname
      ),
      productcategory (
        productcategoryid,
        color,
        agesize
      ),
      suppliers (
        suppliername
      )
    `)
    .eq("businessid", businessId)   // ✅ CONSISTENT
    .not("dateInherited", "is", null)
    .order("dateInherited", { ascending: false });

  if (error) throw error;
  return data;
};
