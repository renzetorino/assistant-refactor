// inventory/fetchDefectiveItems.js
import { supabase } from "../supabase";

export const fetchDefectiveItems = async () => {
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
      products (
        productname,
        image_url
      ),
      productcategory (
        price,
        cost,
        color,
        agesize,
        currentstock,
        reorderpoint
      )
    `)
    .order("updatedat", { ascending: false });

  if (error) {
    console.error("Error fetching defective items:", error);
    return [];
  }

  return data;
};

