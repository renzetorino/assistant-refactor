// inventory/fetchDefectiveItems.js
import { supabase } from "../supabase";

export const fetchDefectiveItems = async (businessid) => {
  if (!businessid) return [];

  const { data, error } = await supabase
    .from("defectiveitems")
    .select(`
      defectiveitemid,
      productid,
      productcategoryid,
      defectdescription,
      status,
      reporteddate,
      quantity,
      products!inner (
        productname,
        image_url,
        businessid
      ),
      productcategory (
        color,
        agesize,
        currentstock,
        reorderpoint
      )
    `)
    .eq("products.businessid", businessid)
    .order("updatedat", { ascending: false });

  if (error) {
    console.error("Error fetching defective items:", error);
    return [];
  }

  return data;
};
