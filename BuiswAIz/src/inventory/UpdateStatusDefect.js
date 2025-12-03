import { supabase } from "../supabase";

const deletionTimers = {};

export const updateDefectStatus = async (defectiveItemId, newStatus, user) => {
  // 1. Update defect status
  const { error: updateError } = await supabase
    .from("defectiveitems")
    .update({ status: newStatus })
    .eq("defectiveitemid", defectiveItemId);

  if (updateError) {
    console.error("Error updating defect status:", updateError.message);
    throw updateError;
  }

  // 2. Fetch product + supplier info
  const { data: defectData, error: defectFetchError } = await supabase
    .from("defectiveitems")
    .select(`
      productid,
      products (
        productname,
        supplierid
      )
    `)
    .eq("defectiveitemid", defectiveItemId)
    .single();

  if (defectFetchError) {
    console.error("Error fetching defect product:", defectFetchError.message);
    return;
  }

  const productName = defectData?.products?.productname || "Unknown Product";
  const supplierId = defectData?.products?.supplierid;

  // 3. Log activity for status change
  if (user) {
    await supabase.from("activitylog").insert([
      {
        action_type: "update_defect_status",
        action_desc: `updated status of ${productName} to ${newStatus}`,
        done_user: user.userid,
      },
    ]);
  }

  // 4. If Returned → increment supplier.defectreturned
  if (newStatus === "Returned" && supplierId) {
    // Get current defectreturned
    const { data: supplierData, error: supplierFetchError } = await supabase
      .from("suppliers")
      .select("defectreturned")
      .eq("supplierid", supplierId)
      .single();

    const { data: defectQtyData, error: defectQtyError } = await supabase
      .from("defectiveitems")
      .select("quantity")
      .eq("defectiveitemid", defectiveItemId)
      .single();

    if (supplierFetchError || defectQtyError) {
      console.error("Error fetching supplier or defect quantity:", supplierFetchError?.message || defectQtyError?.message);
    } else {
      const currentCount = supplierData?.defectreturned || 0;
      const qty = defectQtyData?.quantity || 1;

      const { error: supplierUpdateError } = await supabase
        .from("suppliers")
        .update({ defectreturned: currentCount + qty })
        .eq("supplierid", supplierId);

      if (supplierUpdateError) {
        console.error("Error updating defectreturned:", supplierUpdateError.message);
      } 
    }
  }


  // 5. Handle deletion for "Returned"
  if (newStatus === "Returned") {
    clearTimeout(deletionTimers[defectiveItemId]);

    deletionTimers[defectiveItemId] = setTimeout(() => {
      (async () => {
        const { data, error: fetchError } = await supabase
          .from("defectiveitems")
          .select("status")
          .eq("defectiveitemid", defectiveItemId)
          .single();

        if (fetchError) {
          console.error("Error checking defect status before delete:", fetchError.message);
          return;
        }

        if (data?.status === "Returned") {
          const { error: deleteError } = await supabase
            .from("defectiveitems")
            .delete()
            .eq("defectiveitemid", defectiveItemId);

          if (!deleteError) {
            console.log(`Defect item ${defectiveItemId} deleted after delay.`);

            if (user) {
              const { error: logError } = await supabase.from("activitylog").insert([
                {
                  action_type: "return_defect",
                  action_desc: `Successfully returned ${productName} to the supplier.`,
                  done_user: user.userid,
                },
              ]);

              if (logError) {
                console.error("Error logging return action:", logError.message);
              }
            }
          } else {
            console.error("Error deleting returned defect:", deleteError.message);
          }
        }
      })();
    }, 15000);
  } else {
    clearTimeout(deletionTimers[defectiveItemId]);
    delete deletionTimers[defectiveItemId];
  }
};
