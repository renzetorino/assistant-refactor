import express from "express";
import cors from "cors";
import dotenv from "dotenv";
import sharp from "sharp";
import multer from "multer";
import { createClient } from "@supabase/supabase-js";
import nodemailer from "nodemailer";
import sgMail from "@sendgrid/mail";


dotenv.config();

const app = express();
const PORT = process.env.PORT || 3001;

sgMail.setApiKey(process.env.SENDGRID_API_KEY);
// Middleware
app.use(cors({ origin: process.env.FRONTEND_URL }));
// Only apply JSON parser for non-file routes
app.use((req, res, next) => {
  if (req.originalUrl.startsWith("/api/update-product")) {
    return next(); // skip JSON parsing for update-product (it uses multer)
  }
  express.json()(req, res, next);
});


// Initialize Supabase client using ANON key
const supabase = createClient(process.env.SUPABASE_URL, process.env.SUPABASE_ANON_KEY);
const upload = multer({ storage: multer.memoryStorage() });

// Health check route
app.get("/", (req, res) => {
  res.send("Backend is running");
});

// TODO: Add your API routes here (e.g., /api/add-product, /api/get-products)
app.post("/api/add-product", upload.single("image"), async (req, res) => {
  try {
    const { productname, description, suppliername, categories, userid } = req.body;

    if (!productname || !description || !suppliername || !categories?.length) {
      return res.status(400).json({ error: "Missing required fields." });
    }

    // 1️⃣ Find supplier
    const { data: supplierData, error: supplierError } = await supabase
      .from("suppliers")
      .select("supplierid")
      .eq("suppliername", suppliername)
      .single();

    if (supplierError || !supplierData) {
      return res.status(404).json({ error: "Supplier not found." });
    }

    // 2️⃣ Handle image upload + compression
    let imageUrl = "";
    if (req.file) {
      const compressedBuffer = await sharp(req.file.buffer)
        .resize(800)
        .webp({ quality: 70 })
        .toBuffer();

      const filePath = `${Date.now()}_${req.file.originalname}.webp`;

      const { error: uploadError } = await supabase.storage
        .from("product-images")
        .upload(filePath, compressedBuffer, {
          contentType: "image/webp",
        });

      if (uploadError) {
        return res.status(500).json({ error: "Failed to upload image." });
      }

      const { data: publicData } = supabase.storage
        .from("product-images")
        .getPublicUrl(filePath);

      imageUrl = publicData.publicUrl;
    }

    // 3️⃣ Insert product
    const { data: productData, error: productError } = await supabase
      .from("products")
      .insert([
        {
          productname,
          description,
          supplierid: supplierData.supplierid,
          image_url: imageUrl,
        },
      ])
      .select("productid")
      .single();

    if (productError) {
      return res.status(500).json({ error: "Failed to create product." });
    }

    // 4️⃣ Parse categories if JSON string (from FormData)
    let parsedCategories = categories;
    if (typeof categories === "string") {
      parsedCategories = JSON.parse(categories);
    }

    // 5️⃣ Insert categories and get IDs
    const categoryInserts = parsedCategories.map((cat) => ({
      productid: productData.productid,
      color: cat.color,
      agesize: cat.agesize,
      cost: parseFloat(cat.cost) || 0,
      price: parseFloat(cat.price) || 0,
      currentstock: parseInt(cat.currentstock) || 0,
      reorderpoint: parseInt(cat.reorderpoint) || 0,
    }));

    const { data: insertedCategories, error: catError } = await supabase
      .from("productcategory")
      .insert(categoryInserts)
      .select("productcategoryid");

    if (catError) {
      return res.status(500).json({ error: "Failed to add categories." });
    }

    // 6️⃣ Automatically create stock settings for each category
    for (let i = 0; i < insertedCategories.length; i++) {
      const categoryId = insertedCategories[i].productcategoryid;
      const maxStock = parseInt(parsedCategories[i].currentstock) || 10; // default 10

      const { error: stockError } = await supabase
        .from("stock_setting")
        .insert([
          {
            productid: productData.productid,
            productcategoryid: categoryId,
            max_stock: maxStock,
          },
        ]);

      if (stockError) console.error("Failed to insert stock setting:", stockError);
    }

    // 7️⃣ Log activity
    if (userid) {
      await supabase.from("activitylog").insert([
        {
          action_type: "add_product",
          action_desc: `added ${productname} with ${insertedCategories.length} categories`,
          done_user: userid,
        },
      ]);
    }

    // 8️⃣ Return success
    return res
      .status(200)
      .json({ message: "Product added successfully.", imageUrl });

  } catch (err) {
    console.error(err);
    return res.status(500).json({ error: "Server error." });
  }
});


app.get("/api/get-suppliers", async (req, res) => {
  try {
    const { data, error } = await supabase
      .from("suppliers")
      .select("*")
      .eq("supplierstatus", "Active");

    if (error) return res.status(500).json({ error: error.message });
    res.json(data);
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Server error" });
  }
});

app.post("/api/update-product", upload.single("image"), async (req, res) => {
  try {
    const { productid, productname, description, supplierid, userid } = req.body;

    if (!productid || !productname || !description || !supplierid) {
      return res.status(400).json({ error: "Missing required fields." });
    }

    let imageUrl = null;

    if (req.file) {
      // 1. Get old image from database
      const { data: oldProduct, error: fetchError } = await supabase
        .from("products")
        .select("image_url")
        .eq("productid", productid)
        .single();

      if (fetchError) {
        console.error(fetchError);
      }

      // 2. Delete old image from Supabase storage
      if (oldProduct?.image_url) {
      // ✅ Extract the correct file path after the bucket name
        const oldPath = oldProduct.image_url.split("/product-images/")[1];

        if (oldPath) {
          const {} = await supabase.storage
            .from("product-images")
            .remove([oldPath]);
        }
      }


      // 3. Compress and upload new image
      const compressedBuffer = await sharp(req.file.buffer)
        .resize(800, null, { fit: "inside" })
        .jpeg({ quality: 60 })
        .toBuffer();

      const filePath = `${Date.now()}_${req.file.originalname}`;

      const { error: uploadError } = await supabase.storage
        .from("product-images")
        .upload(filePath, compressedBuffer, { contentType: "image/jpeg" });

      if (uploadError) return res.status(500).json({ error: "Failed to upload image." });

      const { data: publicData } = supabase.storage
        .from("product-images")
        .getPublicUrl(filePath);

      imageUrl = publicData.publicUrl;
    }

    // 4. Update product in DB
    const { error: updateError } = await supabase
      .from("products")
      .update({
        productname,
        description,
        supplierid,
        ...(imageUrl && { image_url: imageUrl }),
        updatedat: new Date().toISOString(),   // 👈 update timestamp
        updatedbyuserid: userid || null,       // 👈 log who updated
      })
      .eq("productid", productid);

    if (updateError) return res.status(500).json({ error: "Failed to update product." });

    // 5. Log activity
    if (userid) {
      await supabase.from("activitylog").insert([
        {
          action_type: "update_product",
          action_desc: `updated ${productname}`,
          done_user: userid,
        },
      ]);
    }

    res.status(200).json({ message: "Product updated successfully.", imageUrl });
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Server error." });
  }
});




app.post("/api/add-category", async (req, res) => {
  try {
    const { productid, color, agesize, cost, price, currentstock, reorderpoint } = req.body;

    if (!productid || !color || !agesize) {
      return res.status(400).json({ error: "Missing required fields." });
    }

    // 1️⃣ Insert new category
    const { data: newCategory, error: insertError } = await supabase
      .from("productcategory")
      .insert([
        {
          productid,
          color,
          agesize,
          cost: parseFloat(cost) || 0,
          price: parseFloat(price) || 0,
          currentstock: parseInt(currentstock) || 0,
          reorderpoint: parseInt(reorderpoint) || 0,
        },
      ])
      .select("productcategoryid")
      .single();

    if (insertError) return res.status(500).json({ error: "Failed to add category." });

    // 2️⃣ Insert corresponding stock_setting row
    const maxStock = parseInt(currentstock) || 10; // default max_stock
    const { error: stockError } = await supabase
      .from("stock_setting")
      .insert([
        {
          productid,
          productcategoryid: newCategory.productcategoryid,
          max_stock: maxStock,
        },
      ]);

    if (stockError) console.error("Failed to insert stock setting:", stockError);

    res.status(200).json({ message: "Category added successfully.", productcategoryid: newCategory.productcategoryid });
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Server error." });
  }
});


app.post("/api/delete-product", async (req, res) => {
  try {
    const { productid, userid } = req.body;

    if (!productid) {
      return res.status(400).json({ error: "Missing product ID." });
    }

    // Optional: Fetch product name for logging
    const { data: productData, error: fetchError } = await supabase
      .from("products")
      .select("productname")
      .eq("productid", productid)
      .single();

    if (fetchError || !productData) {
      return res.status(404).json({ error: "Product not found." });
    }
    
     const { data: oldProduct, error: oldError } = await supabase
      .from("products")
      .select("image_url")
      .eq("productid", productid)
      .single();

    if (oldProduct?.image_url) {
      const oldPath = oldProduct.image_url.split("/product-images/")[1];
      if (oldPath) {
        await supabase.storage.from("product-images").remove([oldPath]);
      }
    }

    // Delete product
    const { error: deleteError } = await supabase
      .from("products")
      .delete()
      .eq("productid", productid);

    if (deleteError) return res.status(500).json({ error: "Failed to delete product." });

    // Optionally delete related categories
    await supabase.from("productcategory").delete().eq("productid", productid);

    // Log activity
    if (userid) {
      await supabase.from("activitylog").insert([
        {
          action_type: "delete_product",
          action_desc: `Deleted ${productData.productname}`,
          done_user: userid,
        },
      ]);
    }

    res.status(200).json({ message: "Product deleted successfully." });
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Server error." });
  }
});


app.post("/api/check-product-deletable", async (req, res) => {
  try {
    const { productid } = req.body;
    if (!productid) {
      return res.status(400).json({ error: "Missing product ID." });
    }

    // Check if product exists
    const { data: productData, error: productError } = await supabase
      .from("products")
      .select("productid, productname")
      .eq("productid", productid)
      .single();

    if (productError || !productData) {
      return res.status(404).json({ error: "Product not found." });
    }

    // Example: Check if product is referenced elsewhere (categories, defects, restock)
    const { data: categories, error: catError } = await supabase
      .from("productcategory")
      .select("productcategoryid")
      .eq("productid", productid);

    if (catError) {
      console.error(catError);
      return res.status(500).json({ error: "Failed to check categories." });
    }

    if (categories.length > 0) {
      return res.status(200).json({
        canDelete: false,
        reason: "Product has categories linked. Please delete categories first.",
      });
    }

    // If no references found, it's safe to delete
    res.status(200).json({ canDelete: true });
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Server error." });
  }
});

app.get("/api/categories/:productid", async (req, res) => {
  try {
    const productid = parseInt(req.params.productid);
    if (isNaN(productid)) return res.status(400).json({ error: "Invalid product ID" });

    const { data, error } = await supabase
      .from("productcategory")
      .select("productcategoryid, price, cost, color, agesize, currentstock, reorderpoint")
      .eq("productid", productid);

    if (error) {
      console.error("Supabase error fetching categories:", error);
      return res.status(500).json({ error: error.message });
    }

    res.json(data || []);
  } catch (err) {
    console.error("Server exception:", err);
    res.status(500).json({ error: "Server error" });
  }
});



// Get all products
app.get("/api/products", async (req, res) => {
  try {
    const { data, error } = await supabase
      .from("products")
      .select("productid, productname")
      .order("productname");

    if (error) return res.status(500).json({ error: error.message });
    res.json(data);
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Server error" });
  }
});

// Add defective item
app.post("/api/add-defective-item", async (req, res) => {
  try {
    const { productid, productcategoryid, quantity, status, defectdescription, reporteddate, userid } = req.body;

    if (!productid || !productcategoryid || !quantity || !status || !reporteddate) {
      return res.status(400).json({ error: "Missing required fields." });
    }

    // Fetch category stock
    const { data: category, error: catErr } = await supabase
      .from("productcategory")
      .select("currentstock")
      .eq("productcategoryid", productcategoryid)
      .single();

    if (catErr || !category) return res.status(400).json({ error: "Category not found." });

    if (parseInt(quantity) > category.currentstock) {
      return res.status(400).json({ error: "Quantity exceeds current stock." });
    }

    // Insert defective item
    const { error: insertErr } = await supabase
      .from("defectiveitems")
      .insert([{
        productid,
        productcategoryid, // use correct column name
        quantity,
        status,
        defectdescription,
        reporteddate
      }]);

    if (insertErr) return res.status(500).json({ error: insertErr.message || JSON.stringify(insertErr) });

    // Update stock
    const { error: updateErr } = await supabase
      .from("productcategory")
      .update({ currentstock: category.currentstock - quantity })
      .eq("productcategoryid", productcategoryid);

    if (updateErr) return res.status(500).json({ error: updateErr.message || JSON.stringify(updateErr) });

    // Log activity
    if (userid) {
      await supabase.from("activitylog").insert([{
        action_type: "add_defect",
        action_desc: `added ${quantity} defective item(s) for product ${productid}, category ${productcategoryid}`,
        done_user: userid
      }]);
    }

    res.status(200).json({ message: "Defective item added successfully." });

  } catch (err) {
    console.error(err);
    res.status(500).json({ error: err.message || "Server error." });
  }
});

// POST /api/restock
app.post("/api/restock", async (req, res) => {
  const {
    productid,
    productcategoryid,
    supplierid,
    new_stock,
    new_cost,
    new_price,
    batchCode,
    datereceived,
    user,
  } = req.body;

  try {
    // Insert restockstorage
    const { error: restockError } = await supabase.from("restockstorage").insert([
      {
        productid,
        productcategoryid,
        supplierid,
        new_stock,
        new_cost,
        new_price,
        batchCode,
        datereceived,
        created_at: new Date().toISOString()
      },
    ]);

    if (restockError) throw restockError;

    // Add expense
    const totalExpense = parseInt(new_stock, 10) * parseFloat(new_cost);
    const expenseDate = new Date();

    await supabase.from("expenses").insert([
      {
        expensedate: expenseDate.toISOString(),
        amount: totalExpense,
        description: `Restock of productid ${productid}`,
        category: "Inventory",
        createdbyuserid: user?.userid || null,
      },
    ]);

    // Add log
    await supabase.from("activitylog").insert([
      {
        action_desc: `Stored product ${productid} to the storage`,
        done_user: user?.userid || null,
      },
    ]);

    res.status(200).json({ success: true, message: "Restock added successfully" });
  } catch (err) {
    console.error("Restock API error:", err.message);
    res.status(500).json({ success: false, error: err.message });
  }
});


app.post("/api/reorder", async (req, res) => {
  try {
    const { productid, productcategoryid, supplierid } = req.body;

    // ✅ Validate input
    if (!productid || !productcategoryid || !supplierid) {
      return res.status(400).json({ error: "Missing required fields." });
    }

    // 1️⃣ Fetch current stock of the category
    const { data: category, error: catError } = await supabase
      .from("productcategory")
      .select("currentstock, cost, color, agesize")
      .eq("productcategoryid", productcategoryid)
      .single();

    if (catError || !category) return res.status(404).json({ error: "Product category not found." });

    // 2️⃣ Fetch max_stock from stock_setting
    const { data: stockSetting, error: stockError } = await supabase
      .from("stock_setting")
      .select("max_stock")
      .eq("productcategoryid", productcategoryid)
      .single();

    if (stockError || !stockSetting) return res.status(500).json({ error: "Stock setting not found." });

    // 3️⃣ Calculate order quantity
    const order_qty = stockSetting.max_stock;
    if (order_qty <= 0) return res.status(400).json({ error: "Stock is already at or above max." });

    // 4️⃣ Check for existing pending orders
    const { data: existingOrder, error: checkError } = await supabase
      .from("purchase_orders")
      .select("purchaseorderid")
      .eq("productid", productid)
      .eq("productcategoryid", productcategoryid)
      .eq("supplierid", supplierid)
      .eq("status", "Pending")
      .maybeSingle();

    if (checkError) return res.status(500).json({ error: "Failed to check existing orders." });
    if (existingOrder) return res.status(400).json({ error: "A pending order already exists for this product/category/supplier." });

    // 5️⃣ Fetch product info
    const { data: product, error: productError } = await supabase
      .from("products")
      .select("productname")
      .eq("productid", productid)
      .single();

    if (productError || !product) return res.status(404).json({ error: "Product not found." });

    // 6️⃣ Fetch supplier info
    const { data: supplier, error: supplierError } = await supabase
      .from("suppliers")
      .select("suppliername, supplieremail")
      .eq("supplierid", supplierid)
      .single();

    if (supplierError || !supplier) return res.status(404).json({ error: "Supplier not found." });

    // 7️⃣ Insert purchase order
    const total_cost = order_qty * category.cost;
    const { data: newOrder, error: orderError } = await supabase
      .from("purchase_orders")
      .insert([{
        productid,
        productcategoryid,
        supplierid,
        order_qty,
        unit_cost: category.cost,
        total_cost,
        status: "Pending",
      }])
      .select()
      .single();

    if (orderError || !newOrder) return res.status(500).json({ error: "Failed to create purchase order." });

    // 8️⃣ Send email to supplier (using SendGrid)
    try {
      const confirmLink = `${process.env.CONFIRM_BASE_URL}/api/confirm-order?purchaseorderid=${newOrder.purchaseorderid}`;
      const rejectLink = `${process.env.CONFIRM_BASE_URL}/api/reject-order?purchaseorderid=${newOrder.purchaseorderid}`;

      const msg = {
        to: supplier.supplieremail,
        from: process.env.SYSTEM_EMAIL, // verified SendGrid sender
        subject: `Reorder Request - ${product.productname}`,
        text: `Hello ${supplier.suppliername},

We would like to reorder:

Product: ${product.productname}
Variant: ${category.color || ""} ${category.agesize || ""}
Quantity: ${order_qty}

Please respond to this order by clicking one of the links below:

✅ Confirm order: ${confirmLink}
❌ Reject order: ${rejectLink}

- IBuisness-Buiswaiz`,
      };

      await sgMail.send(msg);
    } catch (emailError) {
      console.error("SendGrid email error:", emailError);
      return res.status(200).json({
        success: true,
        message: "Purchase order created, but failed to send email via SendGrid.",
        purchaseOrderId: newOrder.purchaseorderid,
      });
    }

    // ✅ Success
    res.status(200).json({
      success: true,
      message: `Reorder of ${order_qty} pcs placed successfully.`,
      purchaseOrderId: newOrder.purchaseorderid,
    });

  } catch (err) {
    console.error("Reorder API error:", err.message);
    res.status(500).json({ error: "Server error." });
  }
});

app.get("/api/confirm-order", async (req, res) => {
  const { purchaseorderid } = req.query;
  if (!purchaseorderid) return res.status(400).send("Missing purchaseorderid");

  // Fetch current status
  const { data: order, error: fetchError } = await supabase
    .from("purchase_orders")
    .select("status")
    .eq("purchaseorderid", purchaseorderid)
    .single();

  if (fetchError || !order) return res.status(404).send("Order not found.");

  if (order.status !== "Pending") {
    return res.status(400).send(`Cannot confirm order. Current status: ${order.status}`);
  }

  // Update only if pending
  const { error } = await supabase
    .from("purchase_orders")
    .update({ status: "Confirmed", confirmed_at: new Date() })
    .eq("purchaseorderid", purchaseorderid);

  if (error) return res.status(500).send("Failed to confirm order.");

  res.send(`<h2>✅ Order ${purchaseorderid} confirmed successfully!</h2>`);
});

// Supplier rejects order
app.get("/api/reject-order", async (req, res) => {
  const { purchaseorderid } = req.query;

  if (!purchaseorderid) {
    return res.status(400).send("Missing purchaseorderid");
  }

  try {
    // Fetch current order status
    const { data: order, error: fetchError } = await supabase
      .from("purchase_orders")
      .select("status")
      .eq("purchaseorderid", purchaseorderid)
      .single();

    if (fetchError || !order) {
      return res.status(404).send("Order not found.");
    }

    //Only allow rejecting if status is Pending
    if (order.status !== "Pending") {
      return res
        .status(400)
        .send(`Cannot reject order. Current status: ${order.status}`);
    }

    //Update order status to Rejected
    const { error } = await supabase
      .from("purchase_orders")
      .update({ status: "Rejected", rejected_at: new Date() })
      .eq("purchaseorderid", purchaseorderid);

    if (error) {
      console.error("Order rejection error:", error);
      return res.status(500).send("❌ Failed to reject order.");
    }

    res.send(`<h2>❌ Order ${purchaseorderid} has been rejected.</h2>`);

  } catch (err) {
    console.error(err);
    res.status(500).send("Server error.");
  }
});

// POST /api/update-defect-status
app.post("/api/update-defect-status", async (req, res) => {
  try {
    const { defectiveItemId, newStatus, userId } = req.body;
    if (!defectiveItemId || !newStatus) {
      return res.status(400).json({ error: "Missing parameters" });
    }

    // 1️⃣ Update defect status in the database
    const { error: updateError } = await supabase
      .from("defectiveitems")
      .update({ status: newStatus })
      .eq("defectiveitemid", defectiveItemId);

    if (updateError) {
      console.error("Error updating defect status:", updateError.message);
      return res.status(500).json({ error: "Failed to update defect status" });
    }

    // 2️⃣ Fetch defect + product + supplier + category info
    const { data: defectData, error: defectFetchError } = await supabase
      .from("defectiveitems")
      .select(`
        quantity,
        defectdescription,
        productcategoryid,
        products (
          productname,
          supplierid
        )
      `)
      .eq("defectiveitemid", defectiveItemId)
      .single();

    if (defectFetchError || !defectData) {
      console.error("Error fetching defect product:", defectFetchError?.message);
      return res.status(500).json({ error: "Failed to fetch defect product" });
    }

    const productName = defectData.products?.productname || "Unknown Product";
    const supplierId = defectData.products?.supplierid;
    const quantity = defectData.quantity || 1;
    const defectDescription = defectData.defectdescription || "N/A";
    const productCategoryId = defectData.productcategoryid;

    // 3️⃣ Log activity
    if (userId) {
      await supabase.from("activitylog").insert([{
        action_type: "update_defect_status",
        action_desc: `updated status of ${productName} to ${newStatus}`,
        done_user: userId,
      }]);
    }

    // 4️⃣ If status is Returned → send email to supplier
    if (newStatus === "Returned" && supplierId) {
      // Fetch supplier info
      const { data: supplierData, error: supplierFetchError } = await supabase
        .from("suppliers")
        .select("suppliername, supplieremail")
        .eq("supplierid", supplierId)
        .single();

      if (supplierFetchError || !supplierData) {
        console.error("Error fetching supplier info:", supplierFetchError?.message);
        return res.status(500).json({ error: "Failed to fetch supplier info" });
      }

      // Fetch product category info
      const { data: categoryData, error: categoryError } = await supabase
        .from("productcategory")
        .select("color, agesize")
        .eq("productcategoryid", productCategoryId)
        .single();

      const categoryColor = categoryData?.color || "N/A";
      const categorySize = categoryData?.agesize || "N/A";

      // Build acknowledgment link
      const ackLink = `${process.env.CONFIRM_BASE_URL}/api/acknowledge-defect?defectiveItemId=${defectiveItemId}&supplierId=${supplierId}`;

      // Send email via SendGrid
      try {
        const msg = {
          to: supplierData.supplieremail,
          from: process.env.SYSTEM_EMAIL,
          subject: `Defective Item Returned - ${productName}`,
          text: `Hello ${supplierData.suppliername},

A defective item has been returned:

Product: ${productName}
Variant: Color: ${categoryColor}, Size/Age: ${categorySize}
Quantity: ${quantity}
Defect Description: ${defectDescription}

Please acknowledge receipt by clicking the link below:

✅ Acknowledge: ${ackLink}

- BuiswAIz`,
        };

        await sgMail.send(msg);
      } catch (err) {
        console.error("SendGrid email error:", err);
      }
    }

    res.json({ success: true, message: `Defect status updated to ${newStatus}` });

  } catch (err) {
    console.error("updateDefectStatus error:", err);
    res.status(500).json({ error: "Server error" });
  }
});


// ---------------------------
// Supplier acknowledgment
// ---------------------------
app.get("/api/acknowledge-defect", async (req, res) => {
  try {
    const { defectiveItemId, supplierId } = req.query;
    if (!defectiveItemId || !supplierId) {
      return res.status(400).send("Missing parameters");
    }

    // Fetch defect item
    const { data: defectItem, error: defectFetchError } = await supabase
      .from("defectiveitems")
      .select("status, quantity, products(productname)")
      .eq("defectiveitemid", defectiveItemId)
      .single();

    if (defectFetchError || !defectItem) {
      return res.status(404).send("Defective item not found");
    }

    if (defectItem.status !== "Returned") {
      return res.status(400).send("Defect item is not marked as Returned");
    }

    const productName = defectItem.products?.productname || "Unknown Product";

    // Increment supplier defectreturned
    const { data: supplierData, error: supplierFetchError } = await supabase
      .from("suppliers")
      .select("defectreturned")
      .eq("supplierid", supplierId)
      .single();

    if (supplierFetchError || !supplierData) {
      return res.status(500).send("Supplier not found");
    }

    const newDefectReturned = (supplierData.defectreturned || 0) + (defectItem.quantity || 1);

    const { error: supplierUpdateError } = await supabase
      .from("suppliers")
      .update({ defectreturned: newDefectReturned })
      .eq("supplierid", supplierId);

    if (supplierUpdateError) {
      console.error("Error updating supplier defectreturned:", supplierUpdateError.message);
      return res.status(500).send("Failed to update supplier defect returned count");
    }

    // Delete defect item after acknowledgment
    const { error: deleteError } = await supabase
      .from("defectiveitems")
      .delete()
      .eq("defectiveitemid", defectiveItemId);

    if (deleteError) {
      console.error("Error deleting defect item:", deleteError.message);
      return res.status(500).send("Failed to delete defect item");
    }

    // Log acknowledgment
    await supabase.from("activitylog").insert([{
      action_type: "acknowledge_defect",
      action_desc: `Supplier acknowledged receipt of ${productName}`,
      done_user: supplierId,
    }]);

    res.send(`<h2>✅ You have acknowledged receipt of ${productName}. The defect item has been removed.</h2>`);

  } catch (err) {
    console.error("acknowledgeDefect error:", err);
    res.status(500).send("Server error");
  }
});




app.listen(PORT, () => {
  console.log(`Server is running on http://localhost:${PORT}`);
});
