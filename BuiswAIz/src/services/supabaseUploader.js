// src/services/supabaseUploader.js
import { supabase } from '../supabase';

/** Convert Excel serial date to ISO string */
function excelDateToISO(serial) {
  if (serial === null || serial === undefined || serial === "") return null;
  const excelEpoch = new Date(1899, 11, 30);
  const msOffset = Number(serial) * 86400000;
  const d = new Date(excelEpoch.getTime() + msOffset);
  return isNaN(d.getTime()) ? null : d.toISOString();
}

function toNumber(v) {
  if (v === null || v === undefined || v === "") return NaN;
  const n = typeof v === 'number' ? v : parseFloat(String(v).toString().replace(/[, ]/g, ''));
  return Number.isFinite(n) ? n : NaN;
}

function toInt(v) {
  const n = toNumber(v);
  return Number.isFinite(n) ? Math.trunc(n) : NaN;
}

function asISODate(v) {
  if (v === null || v === undefined || v === "") return null;
  if (typeof v === "number") return excelDateToISO(v);
  const d = new Date(v);
  return isNaN(d.getTime()) ? null : d.toISOString();
}

function round2(n) {
  return Math.round((n + Number.EPSILON) * 100) / 100;
}

function keyVariant(pid, color, agesize) {
  return `${pid}__${String(color||'').trim().toLowerCase()}__${String(agesize||'').trim().toLowerCase()}`;
}

async function preloadCatalog() {
  // products: name -> productid
  const { data: products, error: perr } = await supabase
    .from('products')
    .select('productid, productname');
  if (perr) throw perr;
  const productNameToId = new Map();
  for (const p of products || []) {
    productNameToId.set(String(p.productname).trim().toLowerCase(), p.productid);
  }

  // productcategory: (productid,color,agesize) -> pc
  const { data: pcs, error: pcerr } = await supabase
    .from('productcategory')
    .select('productcategoryid, productid, price, color, agesize, currentstock');
  if (pcerr) throw pcerr;
  const variantMap = new Map();
  for (const v of pcs || []) {
    variantMap.set(keyVariant(v.productid, v.color, v.agesize), v);
  }

  return { productNameToId, variantMap };
}

/**
 * Validate spreadsheet rows and prepare grouped payload
 * @param {Array<Object>} rows raw rows with keys per REQUIRED_COLUMNS
 * @returns {Object} report: { rows: [{...row, errors:[{field,message}]}], groups: [...], warnings:[], prepared:[] }
 */
export async function validateSpreadsheetRows(rows) {
  const warnings = [];
  const rowReports = rows.map(r => ({ ...r, errors: [] }));

  const { productNameToId, variantMap } = await preloadCatalog();

  // Pass 1: per-row validation and normalization
  for (let i = 0; i < rowReports.length; i++) {
    const r = rowReports[i];

    const orderid = String(r.orderid ?? "").trim();
    if (!orderid) r.errors.push({ field: "orderid", message: "orderid is required" });

    const pname = String(r.productname ?? "").trim();
    if (!pname) {
      r.errors.push({ field: "productname", message: "productname is required" });
    }
    const pid = productNameToId.get(pname.toLowerCase());
    if (!pid) {
      r.errors.push({ field: "productname", message: "Unknown productname (not found in products)" });
    }

    const color = (r.color ?? "").toString().trim();
    if (!color) r.errors.push({ field: "color", message: "color is required" });

    const agesize = (r.agesize ?? "").toString().trim();
    if (!agesize) r.errors.push({ field: "agesize", message: "agesize is required" });

    // Quantity
    const qty = toInt(r.quantity);
    if (!Number.isFinite(qty) || qty <= 0) {
      r.errors.push({ field: "quantity", message: "quantity must be a positive integer" });
    }

    // Prices
    const unit = toNumber(r.unitprice);
    if (!Number.isFinite(unit)) {
      r.errors.push({ field: "unitprice", message: "unitprice must be numeric" });
    }
    const sub = toNumber(r.subtotal);
    if (!Number.isFinite(sub)) {
      r.errors.push({ field: "subtotal", message: "subtotal must be numeric" });
    }

    // Date
    const orderISO = asISODate(r.orderdate);
    if (!orderISO) {
      r.errors.push({ field: "orderdate", message: "orderdate must be a valid date or Excel serial" });
    }

    // Amount paid
    const paid = toNumber(r.amountpaid);
    if (!Number.isFinite(paid)) {
      r.errors.push({ field: "amountpaid", message: "amountpaid must be numeric" });
    }

    // Variant lookup (only if we have pid)
    let pc = null;
    if (pid) {
      pc = variantMap.get(keyVariant(pid, color, agesize));
      if (!pc) {
        r.errors.push({ field: "color", message: "No matching (productname,color,agesize) in productcategory" });
      } else {
        // Optional warnings panelists expect:
        if (Number.isFinite(unit) && Number.isFinite(pc.price) && Math.abs(unit - pc.price) > 0.009) {
          warnings.push(`Row ${r.__row || i + 2}: unitprice ${unit} differs from catalog price ${pc.price} for ${pname}/${color}/${agesize}.`);
        }
        if (Number.isFinite(pc.currentstock) && Number.isFinite(qty) && qty > pc.currentstock) {
          warnings.push(`Row ${r.__row || i + 2}: quantity ${qty} exceeds current stock ${pc.currentstock} for ${pname}/${color}/${agesize}.`);
        }
      }
    }

    // Subtotal consistency
    if (Number.isFinite(qty) && Number.isFinite(unit) && Number.isFinite(sub)) {
      const expect = round2(qty * unit);
      if (Math.abs(expect - round2(sub)) > 0.009) {
        r.errors.push({ field: "subtotal", message: `subtotal should be ${expect} (quantity * unitprice)` });
      }
    }

    // Store normalized/coerced values for later stages
    r.__normalized = {
      sheetOrderId: orderid,
      productid: pid || null,
      productcategoryid: pc?.productcategoryid || null,
      quantity: Number.isFinite(qty) ? qty : null,
      unitprice: Number.isFinite(unit) ? round2(unit) : null,
      subtotal: Number.isFinite(sub) ? round2(sub) : null,
      amountpaid: Number.isFinite(paid) ? round2(paid) : null,
      orderdateISO: orderISO,
    };
  }

  // Group by sheet orderid
  const groupMap = new Map();
  for (const r of rowReports) {
    const gid = r.__normalized?.sheetOrderId || "__invalid__";
    if (!groupMap.has(gid)) groupMap.set(gid, []);
    groupMap.get(gid).push(r);
  }

  const groups = [];
  for (const [gid, rowsInGroup] of groupMap.entries()) {
    const group = { sheetOrderId: gid, errors: [], totalamount: 0, amountpaid: null, orderdateISO: null, status: null, change: 0 };
    let total = 0;
    let paid = null;
    let dateISO = null;

    for (const r of rowsInGroup) {
      total += Number(r.__normalized?.subtotal || 0);
      if (paid === null && r.__normalized?.amountpaid !== null) paid = r.__normalized.amountpaid;
      else if (paid !== null && r.__normalized?.amountpaid !== null && Math.abs(paid - r.__normalized.amountpaid) > 0.009) {
        group.errors.push({ message: `amountpaid is inconsistent within order ${gid}` });
      }

      if (!dateISO && r.__normalized?.orderdateISO) dateISO = r.__normalized.orderdateISO;
      else if (dateISO && r.__normalized?.orderdateISO && new Date(dateISO).toDateString() !== new Date(r.__normalized.orderdateISO).toDateString()) {
        warnings.push(`Order ${gid}: multiple different orderdate values detected; using the first.`);
      }
    }

    group.totalamount = round2(total);
    group.amountpaid = round2(paid ?? 0);
    group.orderdateISO = dateISO || new Date().toISOString();
    group.status = group.amountpaid + 0.0001 >= group.totalamount ? 'complete' : 'incomplete';
    group.change = round2(Math.max(group.amountpaid - group.totalamount, 0));

    groups.push(group);
  }

  const hasRowErrors = rowReports.some(r => r.errors.length > 0);
  const hasGroupErrors = groups.some(g => g.errors.length > 0);

  return {
    rows: rowReports,
    groups,
    warnings,
    prepared: hasRowErrors || hasGroupErrors ? [] : groups.map(g => ({
      sheetOrderId: g.sheetOrderId,
      totalamount: g.totalamount,
      orderstatus: g.status,
      amount_paid: g.amountpaid,
      change: g.change,
      orderdate: g.orderdateISO,
      items: (groupMap.get(g.sheetOrderId) || []).map(r => ({
        productid: r.__normalized.productid,
        productcategoryid: r.__normalized.productcategoryid,
        quantity: r.__normalized.quantity,
        unitprice: r.__normalized.unitprice,
        subtotal: r.__normalized.subtotal,
      }))
    })),
  };
}

/**
 * Upload a previously validated report (from validateSpreadsheetRows)
 * Will assign fresh DB orderid values and insert into orders + orderitems.
 */
export async function uploadValidatedData(report) {
  try {
    if (!report || !Array.isArray(report.prepared) || report.prepared.length === 0) {
      return { success: false, error: { message: "No valid data to upload." } };
    }

    // Find current max orderid
    const { data: maxRows, error: maxErr } = await supabase
      .from('orders')
      .select('orderid')
      .order('orderid', { ascending: false })
      .limit(1);

    if (maxErr) throw maxErr;
    let nextOrderId = (maxRows?.[0]?.orderid || 0) + 1;

    for (const group of report.prepared) {
      // Insert into orders
      const orderPayload = {
        orderid: nextOrderId, // explicitly set to keep continuity
        orderdate: group.orderdate,
        totalamount: group.totalamount,
        orderstatus: group.orderstatus,
        amount_paid: group.amount_paid,
        change: group.change,
      };

      const { error: orderErr } = await supabase.from('orders').insert([orderPayload]);
      if (orderErr) throw orderErr;


      // Decrement stock for each item (atomic on the DB side)
     for (const it of group.items) {
       const { error: decErr } = await supabase.rpc('decrement_stock', {
          p_productcategoryid: it.productcategoryid,
          p_qty: it.quantity,
        });
        if (decErr) throw decErr; // stop if any item can't be decremented
      }

      // Insert orderitems
      const itemsPayload = group.items.map(it => ({
        orderid: nextOrderId,
        productid: it.productid,
        productcategoryid: it.productcategoryid,
        quantity: it.quantity,
        unitprice: it.unitprice,
        subtotal: it.subtotal,
      }));

      const { error: itemsErr } = await supabase.from('orderitems').insert(itemsPayload);
      if (itemsErr) throw itemsErr;

      nextOrderId++;
    }

    return { success: true };
  } catch (error) {
    console.error("Upload to Supabase failed:", error);
    return { success: false, error };
  }
}
