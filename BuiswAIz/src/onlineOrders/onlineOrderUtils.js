import { supabase } from "../supabase";

/** Lowercase keys used for logic, filters, and CSS classes */
export const ONLINE_STATUS_KEYS = ["pending", "preparing", "shipped", "delivered"];

/** Exact values stored in OnlineOrderStatus.OrderStatus */
export const ONLINE_STATUS_DB = {
  pending: "Pending",
  preparing: "Preparing",
  shipped: "Shipped",
  delivered: "Delivered",
};

export const ONLINE_STATUSES = ONLINE_STATUS_KEYS.map((key) => ({
  key,
  label: ONLINE_STATUS_DB[key],
}));

export const normalizeStatus = (status) => {
  const raw = (status || "Pending").toString().trim().toLowerCase();
  if (ONLINE_STATUS_KEYS.includes(raw)) return raw;
  return "pending";
};

/** Format status for Supabase (Pending, Preparing, Shipped, Delivered) */
export const toDbStatus = (status) =>
  ONLINE_STATUS_DB[normalizeStatus(status)] ?? "Pending";

export const getNextStatus = (current) => {
  const idx = ONLINE_STATUS_KEYS.indexOf(normalizeStatus(current));
  if (idx < 0 || idx >= ONLINE_STATUS_KEYS.length - 1) return null;
  return ONLINE_STATUS_KEYS[idx + 1];
};

export const generateUniqueOrderId = async () => {
  const { data, error } = await supabase
    .from("orders")
    .select("orderid")
    .order("orderid", { ascending: false })
    .limit(1);

  if (error) throw new Error(`Database error: ${error.message}`);
  if (!data || data.length === 0) return 1;

  const highestOrderId = parseInt(data[0].orderid, 10);
  return Number.isNaN(highestOrderId) ? 1 : highestOrderId + 1;
};

export const generateOrderCode = async (businessId) => {
  const { count, error } = await supabase
    .from("orders")
    .select("*", { count: "exact", head: true })
    .eq("businessid", businessId);

  if (error) throw error;

  const nextOrderNumber = (count || 0) + 1;
  const orderCode = `ORDER-${nextOrderNumber}`;

  const { data: existing, error: checkError } = await supabase
    .from("orders")
    .select("ordercode")
    .eq("ordercode", orderCode)
    .eq("businessid", businessId)
    .maybeSingle();

  if (checkError && checkError.code !== "PGRST116") throw checkError;

  if (existing) {
    const timestamp = Date.now().toString(36).toUpperCase();
    return `ORDER-${nextOrderNumber}-${timestamp}`;
  }

  return orderCode;
};

/** Create POS-style sale when an online order is marked delivered. */
export const recordDeliveredOrderAsSale = async ({
  lineItems,
  user,
  businessId,
}) => {
  const total = lineItems.reduce(
    (sum, row) => sum + Number(row.subtotal || row.unitprice * row.quantity || 0),
    0
  );

  const uniqueOrderId = await generateUniqueOrderId();
  const orderCode = await generateOrderCode(businessId);
  const now = new Date();
  const orderDateTime = now.toISOString().slice(0, 19).replace("T", " ");

  const orderRecord = {
    orderid: uniqueOrderId,
    ordercode: orderCode,
    totalamount: total,
    orderdate: orderDateTime,
    orderstatus: "COMPLETE",
    amount_paid: total,
    change: 0,
    userid: user.userid,
    businessid: businessId,
  };

  const { error: orderError } = await supabase
    .from("orders")
    .insert([orderRecord]);

  if (orderError) throw new Error(`Order creation failed: ${orderError.message}`);

  const orderItems = lineItems.map((row) => ({
    orderid: uniqueOrderId,
    productid: row.product,
    productcategoryid: row.productcategoryid,
    quantity: row.quantity,
    unitprice: row.unitprice,
    subtotal: row.subtotal ?? row.unitprice * row.quantity,
    createdat: orderDateTime,
  }));

  const { error: itemsError } = await supabase
    .from("orderitems")
    .insert(orderItems);

  if (itemsError) throw new Error(`Order items creation failed: ${itemsError.message}`);

  for (const row of lineItems) {
    const category = row.productcategory;
    const currentStock = category?.currentstock ?? 0;
    const newStock = Math.max(0, currentStock - row.quantity);

    const { error: stockError } = await supabase
      .from("productcategory")
      .update({
        currentstock: newStock,
        updatedstock: orderDateTime,
      })
      .eq("productcategoryid", row.productcategoryid);

    if (stockError) throw new Error(`Stock update failed: ${stockError.message}`);
  }

  return { orderId: uniqueOrderId, orderCode, total };
};
