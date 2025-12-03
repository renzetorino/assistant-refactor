import { supabase } from "../supabase";

export const fetchActiveBatches = async () => {
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
      products (productid, productname),
      productcategory (productcategoryid, color, agesize, currentstock, reorderpoint),
      suppliers (suppliername)
    `)
    .is("dateInherited", null)
    .order("created_at", { ascending: false });

  if (error) throw error;
  return data;
};

export const fetchInheritedBatches = async () => {
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
      products (productid, productname),
      productcategory (productcategoryid, color, agesize),
      suppliers (suppliername)
    `)
    .not("dateInherited", "is", null)
    .order("dateInherited", { ascending: false });

  if (error) throw error;
  return data;
};
