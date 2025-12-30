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

app.post("/api/add-product", upload.single("image"), async (req, res) => {
  try {
    const {
      productname,
      description,
      suppliername,
      categories,
      userid, // coming from frontend
    } = req.body;

    // 🔴 Basic validation
    if (!productname || !suppliername || !categories || !userid) {
      return res.status(400).json({ error: "Missing required fields." });
    }

    // 1️⃣ Validate user & get business
    const { data: userData, error: userError } = await supabase
      .from("systemuser")
      .select("userid, business_id")
      .eq("userid", userid)
      .single();

    if (userError || !userData) {
      return res.status(401).json({ error: "Invalid user." });
    }

    const businessid = userData.business_id;
    if (!businessid) {
      return res.status(400).json({ error: "User is not linked to a business." });
    }

    // 2️⃣ Find supplier in SAME business
    const { data: supplierData, error: supplierError } = await supabase
      .from("suppliers")
      .select("supplierid")
      .eq("suppliername", suppliername)
      .eq("businessid", businessid)
      .single();

    if (supplierError || !supplierData) {
      return res
        .status(404)
        .json({ error: "Supplier not found for this business." });
    }

    // 3️⃣ Handle image upload + compression
    let imageUrl = null;
    if (req.file) {
      const compressedBuffer = await sharp(req.file.buffer)
        .resize(800)
        .webp({ quality: 70 })
        .toBuffer();

      const filePath = `products/${Date.now()}_${productname}.webp`;

      const { error: uploadError } = await supabase.storage
        .from("product-images")
        .upload(filePath, compressedBuffer, { contentType: "image/webp" });

      if (uploadError) return res.status(500).json({ error: "Image upload failed." });

      const { data: publicData } = supabase.storage
        .from("product-images")
        .getPublicUrl(filePath);

      imageUrl = publicData.publicUrl;
    }

    // 4️⃣ Insert product (WITH USER + BUSINESS)
    const { data: productData, error: productError } = await supabase
      .from("products")
      .insert([{
        productname,
        description,
        supplierid: supplierData.supplierid,
        image_url: imageUrl,
        businessid,
        createdbyuserid: userid,
        updatedbyuserid: userid,
      }])
      .select("productid")
      .single();

    if (productError) {
      console.error(productError);
      return res.status(500).json({ error: "Failed to create product." });
    }

    // 5️⃣ Parse categories (FormData safe)
    let parsedCategories = categories;
    if (typeof categories === "string") parsedCategories = JSON.parse(categories);

    if (!Array.isArray(parsedCategories) || parsedCategories.length === 0) {
      return res.status(400).json({ error: "At least one category is required." });
    }

    // 6️⃣ Insert product categories
    const categoryInserts = parsedCategories.map((cat) => ({
      productid: productData.productid,
      color: cat.color || null,
      agesize: cat.agesize || null,
      cost: Number(cat.cost) || 0,
      price: Number(cat.price) || 0,
      currentstock: Number(cat.currentstock) || 0,
      reorderpoint: Number(cat.reorderpoint) || 0,
    }));

    const { data: insertedCategories, error: categoryError } = await supabase
      .from("productcategory")
      .insert(categoryInserts)
      .select(); // select returns inserted rows

    if (categoryError) {
      console.error(categoryError);
      return res.status(500).json({ error: "Failed to add product categories." });
    }

    // ✅ 6.1 Automatically create stock_setting for each category
    for (let cat of insertedCategories) {
      const { error: stockError } = await supabase
        .from("stock_setting")
        .insert([{
          productid: productData.productid,
          productcategoryid: cat.productcategoryid,
          max_stock: 50, // default max stock
        }]);
      if (stockError) console.error("Failed to create stock_setting:", stockError);
    }

    // 7️⃣ Activity log
    await supabase.from("activitylog").insert([{
      action_type: "add_product",
      action_desc: `added ${productname}`,
      done_user: userid,
      businessid,
    }]);

    // 8️⃣ Done
    return res.status(201).json({
      message: "Product added successfully.",
      productid: productData.productid,
      imageUrl,
    });

  } catch (err) {
    console.error("ADD PRODUCT ERROR:", err);
    return res.status(500).json({ error: "Server error." });
  }
});




app.get("/api/get-suppliers", async (req, res) => {
  try {
    const { businessId } = req.query;

    if (!businessId) {
      return res.status(400).json({ error: "businessId is required" });
    }

    const { data, error } = await supabase
      .from("suppliers")
      .select("*")
      .eq("supplierstatus", "Active")
      .eq("businessid", businessId);

    if (error) {
      return res.status(500).json({ error: error.message });
    }

    res.json(data);
  } catch (err) {
    console.error("Get suppliers error:", err);
    res.status(500).json({ error: "Server error" });
  }
});



app.post("/api/update-product", upload.single("image"), async (req, res) => {
  try {
    const { productid, productname, description, supplierid, userid } = req.body;

    if (!productid || !productname || !description || !supplierid || !userid) {
      return res.status(400).json({ error: "Missing required fields." });
    }

    /* 1️⃣ Get user's businessid */
    const { data: userProfile, error: userError } = await supabase
      .from("systemuser")
      .select("business_id")
      .eq("userid", userid)
      .single();

    if (userError || !userProfile) {
      return res.status(403).json({ error: "User not found." });
    }

    const businessid = userProfile.business_id;

    /* 2️⃣ Verify product belongs to same business */
    const { data: product, error: productError } = await supabase
      .from("products")
      .select("image_url, businessid")
      .eq("productid", productid)
      .single();

    if (productError || !product) {
      return res.status(404).json({ error: "Product not found." });
    }

    if (product.businessid !== businessid) {
      return res.status(403).json({ error: "Unauthorized product access." });
    }

    let imageUrl = product.image_url;

    /* 3️⃣ Handle image update */
    if (req.file) {
      // Delete old image
      if (product.image_url) {
        const oldPath = product.image_url.split("/product-images/")[1];
        if (oldPath) {
          await supabase.storage.from("product-images").remove([oldPath]);
        }
      }

      // Compress & upload new image
      const compressedBuffer = await sharp(req.file.buffer)
        .resize(800, null, { fit: "inside" })
        .jpeg({ quality: 60 })
        .toBuffer();

      const filePath = `${businessid}/${Date.now()}_${req.file.originalname}`;

      const { error: uploadError } = await supabase.storage
        .from("product-images")
        .upload(filePath, compressedBuffer, {
          contentType: "image/jpeg",
        });

      if (uploadError) {
        return res.status(500).json({ error: "Failed to upload image." });
      }

      const { data: publicData } = supabase.storage
        .from("product-images")
        .getPublicUrl(filePath);

      imageUrl = publicData.publicUrl;
    }

    /* 4️⃣ Update product (BUSINESS-SAFE) */
    const { error: updateError } = await supabase
      .from("products")
      .update({
        productname,
        description,
        supplierid,
        image_url: imageUrl,
        updatedat: new Date().toISOString(),
        updatedbyuserid: userid,
      })
      .eq("productid", productid)
      .eq("businessid", businessid); // 🔥 IMPORTANT

    if (updateError) {
      return res.status(500).json({ error: "Failed to update product." });
    }

    /* 5️⃣ Log activity */
    await supabase.from("activitylog").insert([
      {
        businessid: businessid,
        action_type: "update_product",
        action_desc: `updated ${productname}`,
        done_user: userid,
      },
    ]);

    res.status(200).json({
      message: "Product updated successfully.",
      imageUrl,
    });
  } catch (err) {
    console.error("Update product error:", err);
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
    const { userid } = req.query;

    if (!userid) {
      return res.status(400).json({ error: "userid is required" });
    }

    // 1️⃣ Get user's business
    const { data: user, error: userErr } = await supabase
      .from("systemuser")
      .select("business_id")
      .eq("userid", userid)
      .single();

    if (userErr || !user) {
      return res.status(403).json({ error: "User not found" });
    }

    const businessid = user.business_id;

    // 2️⃣ Fetch products ONLY for this business
    const { data, error } = await supabase
      .from("products")
      .select("productid, productname")
      .eq("businessid", businessid)
      .order("productname");

    if (error) {
      return res.status(500).json({ error: error.message });
    }

    res.json(data);
  } catch (err) {
    console.error("Get products error:", err);
    res.status(500).json({ error: "Server error" });
  }
});


// Add defective item
app.post("/api/add-defective-item", async (req, res) => {
  try {
    const {
      productid,
      productcategoryid,
      quantity,
      status,
      defectdescription,
      reporteddate,
      userid,
    } = req.body;

    if (
      !productid ||
      !productcategoryid ||
      !quantity ||
      !status ||
      !reporteddate ||
      !userid
    ) {
      return res.status(400).json({ error: "Missing required fields." });
    }

    /* 1️⃣ Get user's business */
    const { data: user, error: userErr } = await supabase
      .from("systemuser")
      .select("business_id")
      .eq("userid", userid)
      .single();

    if (userErr || !user) {
      return res.status(403).json({ error: "User not found." });
    }

    const businessid = user.business_id;

    /* 2️⃣ Get category (NO businessid here) */
    const { data: category, error: catErr } = await supabase
      .from("productcategory")
      .select("currentstock, productid")
      .eq("productcategoryid", productcategoryid)
      .single();

    if (catErr || !category) {
      return res.status(404).json({ error: "Category not found." });
    }

    /* 3️⃣ Verify product belongs to user's business */
    const { data: product, error: prodErr } = await supabase
      .from("products")
      .select("businessid")
      .eq("productid", category.productid)
      .single();

    if (prodErr || !product) {
      return res.status(404).json({ error: "Product not found." });
    }

    if (product.businessid !== businessid) {
      return res.status(403).json({ error: "Unauthorized access." });
    }

    if (parseInt(quantity) > category.currentstock) {
      return res.status(400).json({ error: "Quantity exceeds current stock." });
    }

    /* 4️⃣ Insert defective item */
    const { error: insertErr } = await supabase.from("defectiveitems").insert([
      {
        productid,
        productcategoryid,
        quantity,
        status,
        defectdescription,
        reporteddate,
        reportedbyuserid: userid,
      },
    ]);

    if (insertErr) {
      return res.status(500).json({ error: insertErr.message });
    }

    /* 5️⃣ Update stock */
    const { error: updateErr } = await supabase
      .from("productcategory")
      .update({
        currentstock: category.currentstock - parseInt(quantity),
      })
      .eq("productcategoryid", productcategoryid);

    if (updateErr) {
      return res.status(500).json({ error: updateErr.message });
    }

    /* 6️⃣ Log activity (WITH businessid) */
    await supabase.from("activitylog").insert([
      {
        businessid,
        action_type: "add_defect",
        action_desc: `added ${quantity} defective item(s)`,
        done_user: userid,
      },
    ]);

    res.status(200).json({ message: "Defective item added successfully." });
  } catch (err) {
    console.error("Add defective error:", err);
    res.status(500).json({ error: "Server error." });
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
    productname,
    user,
  } = req.body;

  try {
    const now = new Date();

    /* ===============================
       1️⃣ INSERT RESTOCK STORAGE
    =============================== */
    const { error: restockError } = await supabase
      .from("restockstorage")
      .insert([
        {
          productid,
          productcategoryid,
          supplierid,
          new_stock,
          new_cost,
          new_price,
          batchCode,
          datereceived,
          created_at: now.toISOString(),

          // 🔐 ownership
          userid: user.userid,
          businessid: user.business_id,
        },
      ]);

    if (restockError) throw restockError;

    /* ===============================
       2️⃣ INSERT EXPENSE
    =============================== */
    const totalExpense =
      Number(new_stock) * Number(new_cost);

    const { error: expenseError } = await supabase
      .from("expenses")
      .insert([
        {
          user_id: user.userid,
          occurred_on: now.toISOString().split("T")[0],
          category_id: "5e4b2625-86ba-4066-adaa-4657700c118c",
          amount: totalExpense,
          notes: `Inventory Restock for ${productname}`,
          status: "cleared",
          business_id: user.business_id
        },
      ]);

    if (expenseError) throw expenseError;

    /* ===============================
       3️⃣ ACTIVITY LOG (OPTIONAL)
    =============================== */
    await supabase.from("activitylog").insert([
      {
        action_desc: `Stored ${productname} to inventory`,
        done_user: user.userid,
        businessid: user.business_id,
      },
    ]);

    res.status(200).json({
      success: true,
      message: "Restock and expense recorded successfully",
    });

  } catch (err) {
    console.error("Restock API error:", err);
    res.status(500).json({
      success: false,
      error: err.message,
    });
  }
});


app.post("/api/reorder", async (req, res) => {
  try {
    const { productid, productcategoryid, userid } = req.body;

    // ✅ Validate input
    if (!productid || !productcategoryid || !userid) {
      return res.status(400).json({ error: "Missing required fields." });
    }

    // 1️⃣ Fetch user & business
    const { data: userData, error: userError } = await supabase
      .from("systemuser")
      .select("userid, business_id")
      .eq("userid", userid)
      .single();
    if (userError || !userData) return res.status(401).json({ error: "Invalid user." });

    const businessid = userData.business_id;

    // 2️⃣ Fetch product info (including supplierid)
    const { data: product, error: productError } = await supabase
      .from("products")
      .select("productname, supplierid")
      .eq("productid", productid)
      .single();
    if (productError || !product) return res.status(404).json({ error: "Product not found." });

    // 3️⃣ Fetch supplier info
    const { data: supplier, error: supplierError } = await supabase
      .from("suppliers")
      .select("suppliername, supplieremail")
      .eq("supplierid", product.supplierid)
      .single();
    if (supplierError || !supplier) return res.status(404).json({ error: "Supplier not found." });

    // 4️⃣ Fetch product category info
    const { data: category, error: catError } = await supabase
      .from("productcategory")
      .select("currentstock, cost, color, agesize")
      .eq("productcategoryid", productcategoryid)
      .single();
    if (catError || !category) return res.status(404).json({ error: "Product category not found." });

    // 5️⃣ Fetch max_stock from stock_setting
    const { data: stockSetting, error: stockError } = await supabase
      .from("stock_setting")
      .select("max_stock")
      .eq("productcategoryid", productcategoryid)
      .eq("productid", productid)
      .single();
    if (stockError || !stockSetting) return res.status(500).json({ error: "Stock setting not found." });

    // 6️⃣ Calculate order quantity
    const order_qty = stockSetting.max_stock;
    if (order_qty <= 0) return res.status(400).json({ error: "Stock is already at or above max." });

    // 7️⃣ Check for existing pending orders
    const { data: existingOrder, error: checkError } = await supabase
      .from("purchase_orders")
      .select("purchaseorderid")
      .eq("productid", productid)
      .eq("productcategoryid", productcategoryid)
      .eq("supplierid", product.supplierid)
      .eq("status", "Pending")
      .maybeSingle();
    if (checkError) return res.status(500).json({ error: "Failed to check existing orders." });
    if (existingOrder) return res.status(400).json({ error: "A pending order already exists for this product/category/supplier." });

    // 8️⃣ Insert purchase order including userid & businessid
    const total_cost = order_qty * category.cost;
    const { data: newOrder, error: orderError } = await supabase
      .from("purchase_orders")
      .insert([{
        productid,
        productcategoryid,
        supplierid: product.supplierid,
        order_qty,
        unit_cost: category.cost,
        total_cost,
        status: "Pending",
        userid,        // ✅ include user who reordered
        businessid,    // ✅ include business
      }])
      .select()
      .single();
    if (orderError || !newOrder) return res.status(500).json({ error: "Failed to create purchase order." });

    // 9️⃣ Send email to supplier
    try {
      const confirmLink = `${process.env.CONFIRM_BASE_URL}/api/confirm-order?purchaseorderid=${newOrder.purchaseorderid}`;
      const rejectLink = `${process.env.CONFIRM_BASE_URL}/api/reject-order?purchaseorderid=${newOrder.purchaseorderid}`;

      await sgMail.send({
        to: supplier.supplieremail,
        from: process.env.SYSTEM_EMAIL,
        subject: `Reorder Request - ${product.productname}`,
        text: `Hello ${supplier.suppliername},

We would like to reorder:

Product: ${product.productname}
Variant: ${category.color || ""} ${category.agesize || ""}
Quantity: ${order_qty}

Please respond by clicking one of the links:

✅ Confirm order: ${confirmLink}
❌ Reject order: ${rejectLink}

- IBuisness-Buiswaiz`,
      });
    } catch (emailError) {
      console.error("SendGrid email error:", emailError);
      return res.status(200).json({
        success: true,
        message: "Purchase order created, but failed to send email.",
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

    if (!defectiveItemId || !newStatus || !userId) {
      return res.status(400).json({ error: "Missing parameters" });
    }

    /* 1️⃣ Get user's business */
    const { data: user, error: userErr } = await supabase
      .from("systemuser")
      .select("business_id")
      .eq("userid", userId)
      .single();

    if (userErr || !user) {
      return res.status(403).json({ error: "User not found." });
    }

    const businessid = user.business_id;

    /* 2️⃣ Fetch defect + product (for authorization) */
    const { data: defectData, error: defectFetchError } = await supabase
      .from("defectiveitems")
      .select(`
        defectiveitemid,
        quantity,
        defectdescription,
        productcategoryid,
        products (
          productid,
          productname,
          supplierid,
          businessid
        )
      `)
      .eq("defectiveitemid", defectiveItemId)
      .single();

    if (defectFetchError || !defectData) {
      return res.status(404).json({ error: "Defective item not found" });
    }

    /* 3️⃣ BUSINESS SECURITY CHECK */
    if (defectData.products.businessid !== businessid) {
      return res.status(403).json({ error: "Unauthorized access" });
    }

    /* 4️⃣ Update defect status */
    const { error: updateError } = await supabase
      .from("defectiveitems")
      .update({ status: newStatus })
      .eq("defectiveitemid", defectiveItemId);

    if (updateError) {
      return res.status(500).json({ error: "Failed to update defect status" });
    }

    const productName = defectData.products.productname;
    const supplierId = defectData.products.supplierid;
    const quantity = defectData.quantity || 1;
    const defectDescription = defectData.defectdescription || "N/A";
    const productCategoryId = defectData.productcategoryid;

    /* 5️⃣ Log activity (WITH businessid ✅) */
    await supabase.from("activitylog").insert([
      {
        businessid: businessid,
        action_type: "update_defect_status",
        action_desc: `updated status of ${productName} to ${newStatus}`,
        done_user: userId,
      },
    ]);

    /* 6️⃣ If Returned → notify supplier */
    if (newStatus === "Returned" && supplierId) {
      const { data: supplierData } = await supabase
        .from("suppliers")
        .select("suppliername, supplieremail")
        .eq("supplierid", supplierId)
        .single();

      const { data: categoryData } = await supabase
        .from("productcategory")
        .select("color, agesize")
        .eq("productcategoryid", productCategoryId)
        .single();

      const ackLink = `${process.env.CONFIRM_BASE_URL}/api/acknowledge-defect?defectiveItemId=${defectiveItemId}&supplierId=${supplierId}`;

      if (supplierData?.supplieremail) {
        await sgMail.send({
          to: supplierData.supplieremail,
          from: process.env.SYSTEM_EMAIL,
          subject: `Defective Item Returned - ${productName}`,
          text: `Hello ${supplierData.suppliername},

A defective item has been returned:

Product: ${productName}
Variant: Color: ${categoryData?.color || "N/A"}, Size/Age: ${categoryData?.agesize || "N/A"}
Quantity: ${quantity}
Defect Description: ${defectDescription}

Acknowledge here:
${ackLink}

- BuiswAIz`,
        });
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

app.get("/api/exchange-products", async (req, res) => {
  try {
    const { supplierid } = req.query;
    if (!supplierid) return res.status(400).json({ error: "supplier_id required" });

    const { data: products, error } = await supabase
      .from("products")
      .select("productid, productname, supplierid")
      .eq("supplierid", supplierid); // <-- use the correct column name

    if (error) {
      console.error("Supabase fetch error:", error);
      return res.status(500).json({ error: "Failed to fetch products" });
    }

    res.json(products);
  } catch (err) {
    console.error("Server error:", err);
    res.status(500).json({ error: "Server error" });
  }
});


app.post("/api/product-exchange", async (req, res) => {
  try {
    const {
      supplier_id,
      old_product,
      old_product_category,
      new_product,
      new_product_category,
      quantity,
      reason,
      user_id, // simplified
    } = req.body;

    // Validate required fields
    if (
      !supplier_id ||
      !old_product ||
      !old_product_category ||
      !new_product ||
      !new_product_category ||
      !quantity ||
      !reason
    ) {
      return res.status(400).json({ error: "All fields are required." });
    }

    // Insert exchange request
    const { data: exchangeData, error: insertError } = await supabase
      .from("productExchange")
      .insert([{
        supplier_id,
        old_product,
        old_product_category,
        new_product,
        new_product_category,
        quantity: Number(quantity),
        reason,
        status: "Pending",
        requested_at: new Date(),
      }])
      .select("exchangeid")
      .single();

    if (insertError || !exchangeData) {
      console.error("Insert exchange error:", insertError);
      return res.status(500).json({ error: "Failed to submit exchange." });
    }

    // Update old product category stock
    const { data: oldCatStockData, error: oldCatStockError } = await supabase
      .from("productcategory")
      .select("currentstock")
      .eq("productcategoryid", old_product_category)
      .single();

    if (oldCatStockError || !oldCatStockData) {
      console.error("Fetch old product category stock error:", oldCatStockError);
    } else {
      await supabase
        .from("productcategory")
        .update({ currentstock: oldCatStockData.currentstock - Number(quantity) })
        .eq("productcategoryid", old_product_category);
    }

    // Fetch supplier info
    const { data: supplierData, error: supplierFetchError } = await supabase
      .from("suppliers")
      .select("suppliername, supplieremail")
      .eq("supplierid", supplier_id)
      .single();

    if (supplierFetchError || !supplierData) {
      console.error("Fetch supplier error:", supplierFetchError);
      return res.status(500).json({ error: "Failed to fetch supplier info." });
    }

    // Fetch product and category names
    const oldProd = await supabase.from("products").select("productname").eq("productid", old_product).single();
    const oldCat = await supabase.from("productcategory").select("color, agesize, currentstock").eq("productcategoryid", old_product_category).single();
    const newProd = await supabase.from("products").select("productname").eq("productid", new_product).single();
    const newCat = await supabase.from("productcategory").select("color, agesize, currentstock").eq("productcategoryid", new_product_category).single();

    // Send confirmation email
    const confirmLink = `${process.env.CONFIRM_BASE_URL}/api/confirm-exchange?exchangeid=${exchangeData.exchangeid}`;
    const msg = {
      to: supplierData.supplieremail,
      from: process.env.SYSTEM_EMAIL,
      subject: "Product Exchange Request",
      text: `Hello ${supplierData.suppliername},

A product exchange request has been submitted:

Old Product: ${oldProd.data.productname} (${oldCat.data.color} ${oldCat.data.agesize})
New Product: ${newProd.data.productname} (${newCat.data.color} ${newCat.data.agesize})
Quantity: ${quantity}
Reason: ${reason}

Please confirm the exchange:
✅ Confirm: ${confirmLink}

- BuiswAIz`,
    };
    await sgMail.send(msg);

    // Log activity if user_id is provided
    if (user_id) {
      await supabase.from("activitylog").insert([{
        action_type: "submit_exchange",
        action_desc: `User submitted exchange request for ${oldProd.data.productname} → ${newProd.data.productname}`,
        done_user: user_id,
      }]);
    }

    res.json({ success: true, message: "Exchange submitted and supplier notified.", exchange: exchangeData });

  } catch (err) {
    console.error("Exchange API error:", err);
    res.status(500).json({ error: "Server error." });
  }
});


// 2️⃣ Supplier confirms exchange
app.get("/api/confirm-exchange", async (req, res) => {
  try {
    const { exchangeid } = req.query;
    if (!exchangeid) return res.status(400).send("Missing exchange ID");

    const exchangeIdNum = Number(exchangeid);

    const { data: exchange, error: fetchError } = await supabase
      .from("productExchange")
      .select("*")
      .eq("exchangeid", exchangeIdNum)
      .single();

    if (fetchError || !exchange) return res.status(404).send("Exchange not found");

    if (exchange.status !== "Pending") {
      return res.status(400).send(`Cannot confirm exchange. Current status: ${exchange.status}`);
    }

    const { error: updateError } = await supabase
      .from("productExchange")
      .update({ status: "Confirmed", confirmed_at: new Date() })
      .eq("exchangeid", exchangeIdNum);

    if (updateError) {
      console.error("Update confirm status error:", updateError);
      return res.status(500).send("Failed to confirm exchange");
    }

    res.send(`<h2>✅ Exchange ${exchangeIdNum} confirmed successfully by supplier!</h2>`);
  } catch (err) {
    console.error("Confirm exchange error:", err);
    res.status(500).send("Server error.");
  }
});



app.listen(PORT, () => {
  console.log(`Server is running on http://localhost:${PORT}`);
});
