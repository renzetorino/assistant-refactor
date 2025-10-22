// UploadSheets.jsx
import React, { useState, useMemo, useEffect } from "react";
import * as XLSX from "xlsx";
import { toast } from "react-toastify";
import { validateSpreadsheetRows, uploadValidatedData } from "../services/supabaseUploader";
import "../stylecss/UploadSheets.css";

// Replace your single REQUIRED_COLUMNS with:
const REQUIRED_COLUMNS = [
  "orderid",
  "productname",
  "quantity",
  "unitprice",
  "amountpaid",
  "orderdate",
  // "subtotal" stays logically required for upload, BUT we can now compute it if absent
];

const OPTIONAL_COLUMNS = [
  "color",
  "agesize",
  "subtotal", // optional in mapping; we’ll compute if missing
];

const FIELD_SYNONYMS = {
  orderid: ["order id","order no","order number","invoice","invoice no","so#","order#","ref no"],
  productname: ["product","item","item name","description","sku name","sku"],
  color: ["colour","variant color","color/variant","shade"],
  agesize: ["age/size","size","age size","dimension"],
  quantity: ["qty","qty.","quantity ordered","units","pcs","pieces"],
  unitprice: ["unit price","price","unit cost","cost/unit","price ea","price each","rate"],
  subtotal: ["line total","amount","gross","total (no tax)","net amount","row total"],
  amountpaid: ["paid","amount paid","payment","received","collected"],
  orderdate: ["date","order date","invoice date","txn date","sales date"]
};


function toISODate(d) {
  // Ensures YYYY-MM-DD (no time)
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

// Excel serial -> Date
function fromExcelSerial(n) {
  // Excel's day 1 is 1899-12-31; Excel incorrectly treats 1900 as leap year.
  const base = new Date(Date.UTC(1899, 11, 30)); // 1899-12-30 UTC handles the bug offset
  const ms = n * 86400000;
  return new Date(base.getTime() + ms);
}

function coerceNumber(val) {
  if (val == null) return null;
  if (typeof val === "number") return isFinite(val) ? val : null;
  const s = String(val).trim();
  if (!s) return null;
  // Strip common currency/commas
  const cleaned = s.replace(/[₱$,]/g, "").replace(/\s/g, "");
  const n = Number(cleaned);
  return isFinite(n) ? n : null;
}

function parseDateFlexible(v) {
  if (v == null || v === "") return null;

  // 1) Numbers: Excel serials
  if (typeof v === "number") {
    const d = fromExcelSerial(v);
    return toISODate(d);
  }

  // 2) Date object
  if (v instanceof Date && !isNaN(v)) {
    return toISODate(v);
  }

  // 3) Strings: try common formats
  const s = String(v).trim();

  // If already ISO-ish YYYY-MM-DD
  const isoMatch = s.match(/^(\d{4})[-/](\d{1,2})[-/](\d{1,2})$/);
  if (isoMatch) {
    const d = new Date(Number(isoMatch[1]), Number(isoMatch[2]) - 1, Number(isoMatch[3]));
    if (!isNaN(d)) return toISODate(d);
  }

  // MM/DD/YY or MM/DD/YYYY (also accepts -)
  const mdys = s.match(/^(\d{1,2})[/-](\d{1,2})[/-](\d{2}|\d{4})$/);
  if (mdys) {
    let yy = Number(mdys[3]);
    if (yy < 100) yy += (yy >= 70 ? 1900 : 2000); // pivot at 1970
    const d = new Date(yy, Number(mdys[1]) - 1, Number(mdys[2]));
    if (!isNaN(d)) return toISODate(d);
  }

  // Fallback: Date.parse
  const d = new Date(s);
  if (!isNaN(d)) return toISODate(d);

  return null; // let validator flag it
}



function normalizeHeader(h) {
  return String(h || "").trim().toLowerCase();
}

function saveMap(map){ try{ localStorage.setItem("upload_header_map_v1", JSON.stringify(map)); }catch{} }
function loadMap(){ try{ return JSON.parse(localStorage.getItem("upload_header_map_v1")||"null"); }catch{ return null; } }


// ⬇️ Changed: make this a named export
export function downloadTemplate() {
  const headers = [
    "orderid",
    "orderdate",
    "productname",
    "color",
    "agesize",
    "quantity",
    "unitprice",
    "subtotal",
    "amountpaid",
  ];

  const sample = [
    "10001",
    "2025-10-04",
    "Basic Tee",
    "Black",
    "M",
    "2",
    "250",
    "500",
    "500"
  ];

  const hasXLSX = typeof window !== "undefined" && window.XLSX;

  if (hasXLSX) {
    const wsData = [headers, sample];
    const ws = window.XLSX.utils.aoa_to_sheet(wsData);
    const wb = window.XLSX.utils.book_new();
    window.XLSX.utils.book_append_sheet(wb, ws, "Sales Upload Template");

    const wbout = window.XLSX.write(wb, { bookType: "xlsx", type: "array" });
    const blob = new Blob([wbout], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });

    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "sales_upload_template.xlsx";
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  } else {
    const rows = [headers, sample];
    const csv = rows.map(r =>
      r.map(v => {
        const s = String(v ?? "");
        if (/[",\n]/.test(s)) return `"${s.replace(/"/g, '""')}"`;
        return s;
      }).join(",")
    ).join("\n");

    const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "sales_upload_template.csv";
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  }
}

// Optional: keep a global for any legacy callers
if (typeof window !== "undefined") {
  window.downloadTemplate = downloadTemplate;
}

function levenshtein(a, b) {
  a = (a||""); b = (b||"");
  const m = Array.from({length:a.length+1}, (_,i)=>[i]);
  for (let j=0;j<=b.length;j++) m[0][j]=j;
  for (let i=1;i<=a.length;i++){
    for (let j=1;j<=b.length;j++){
      const cost = a[i-1]===b[j-1] ? 0 : 1;
      m[i][j] = Math.min(m[i-1][j]+1, m[i][j-1]+1, m[i-1][j-1]+cost);
    }
  }
  return m[a.length][b.length];
}

function scoreSimilarity(requiredKey, header) {
  const r = normalizeHeader(requiredKey);
  const h = normalizeHeader(header);
  if (!h) return 0;
  if (h === r) return 1.0;
  if (FIELD_SYNONYMS[r]?.includes(h)) return 0.95;
  if (h.includes(r) || r.includes(h)) return 0.85;
  const dist = levenshtein(r, h);
  const maxLen = Math.max(r.length, h.length) || 1;
  const sim = 1 - dist / maxLen;    // 0..1
  return Math.max(0, Math.min(0.8, sim));
}

function guessHeaderMap(sheetHeaders) {
  const headers = sheetHeaders.map(normalizeHeader);
  const unique = Array.from(new Set(headers));
  const picks = {};
  [...REQUIRED_COLUMNS, ...OPTIONAL_COLUMNS].forEach(req => {
    let best = null, bestScore = 0;
    unique.forEach(h => {
      const s = scoreSimilarity(req, h);
      if (s > bestScore) { best = h; bestScore = s; }
    });
    picks[req] = best || null;
  });
  return picks;
}




function UploadSheets() {
  const [rawRows, setRawRows] = useState([]);
  const [rows, setRows] = useState([]);
  const [validReport, setValidReport] = useState(null);
  const [loading, setLoading] = useState(false);
  const [fileName, setFileName] = useState("");
  const [warnings, setWarnings] = useState([]);
  const [detectedHeaders, setDetectedHeaders] = useState([]);
  const [headerMap, setHeaderMap] = useState(null); // { orderid: "order no", ... }
  const [stage, setStage] = useState("idle"); // idle | map | ready
  const [lastSheet, setLastSheet] = useState(null);

  useEffect(() => {
    const prior = loadMap();
    if (prior) {
      const normalized = Object.fromEntries(Object.entries(prior).map(([k,v]) => [k, normalizeHeader(v)]));
      setHeaderMap(m => ({ ...(m || {}), ...normalized }));
    }
  }, []);


  const allValid = useMemo(() => {
    if (!validReport) return false;
    const rowsOk = Array.isArray(validReport.rows)
      ? validReport.rows.every(r => !r.errors || r.errors.length === 0)
      : true;
    const groupsOk = Array.isArray(validReport.groups)
      ? validReport.groups.every(g => !g.errors || g.errors.length === 0)
      : true;
    return rowsOk && groupsOk;
  }, [validReport]);

  const handleFile = async (e) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setFileName(file.name);
    setLoading(true);
    try {
      const data = await file.arrayBuffer();
      const wb = XLSX.read(data, { type: "array" });
      const ws = wb.Sheets[wb.SheetNames[0]];
      const json = XLSX.utils.sheet_to_json(ws, { header: 1, raw: true });
      setLastSheet(json);
      if (!json.length) throw new Error("Empty sheet");

      const sheetHeaders = json[0];
      setDetectedHeaders(sheetHeaders);

      const initial = guessHeaderMap(sheetHeaders);
      setHeaderMap(initial);
      setValidReport(null);
      setWarnings([]);
      setRawRows([]);
      setRows([]);
      setStage("map");  

    } catch (err) {
      console.error(err);
      toast.error(err.message || "Failed to parse spreadsheet");
    } finally {
      setLoading(false);
    }
  };


  const applyMapping = async (sheet) => {
    if (!headerMap || !detectedHeaders?.length) return;
    const unmappedReq = REQUIRED_COLUMNS.filter(k => !headerMap[k]);
    if (unmappedReq.length) {
      toast.error(`Please map required fields: ${unmappedReq.join(", ")}`);
      return;
    }
    const idx = Object.fromEntries(detectedHeaders.map((h,i) => [normalizeHeader(h), i]));
    const pick = (row, key) => {
      const m = headerMap[key];
      if (!m) return null;
      const i = idx[m];
      return i == null ? null : row[i];
    };
    const body = sheet.slice(1).filter(r => r && r.some(v => v != null && String(v).trim() !== ""));
    const parsed = body.map((r, rowIndex) => {
      const quantity   = coerceNumber(pick(r, "quantity"));
      const unitprice  = coerceNumber(pick(r, "unitprice"));
      const amountpaid = coerceNumber(pick(r, "amountpaid"));
      const subtotalFromSheet = coerceNumber(pick(r, "subtotal"));
      const subtotal = (subtotalFromSheet == null && quantity != null && unitprice != null)
        ? Number((quantity * unitprice).toFixed(2))
        : subtotalFromSheet;
      const orderdate = parseDateFlexible(pick(r, "orderdate"));
      return {
        __row: rowIndex + 2,
        orderid: pick(r, "orderid"),
        productname: pick(r, "productname"),
        color: pick(r, "color"),
        agesize: pick(r, "agesize"),
        quantity, unitprice, subtotal, amountpaid, orderdate
      };
    });
    setRawRows(parsed);
    setRows(parsed);
    saveMap(headerMap);
    setLoading(true);
    try{
      const report = await validateSpreadsheetRows(parsed);
      setValidReport(report);
      setWarnings(report.warnings || []);
      setStage("ready");
      if (report.rows?.some(r => r.errors?.length)) {
        toast.warn("Some rows need fixes. Please edit inline until all errors are resolved.");
      } else if (report.groups?.some(g => g.errors?.length)) {
        toast.warn("Some order groups have issues. Please fix them.");
      } else {
        toast.success("Looks good! You can upload.");
      }
    } finally {
      setLoading(false);
    }
  };

  const onCellChange = async (rowIdx, key, value) => {
    let v = value;
    if (["quantity","unitprice","subtotal","amountpaid"].includes(key)) v = coerceNumber(value);
    if (key === "orderdate") v = parseDateFlexible(value);
    const updated = rows.map((r, i) => (i === rowIdx ? { ...r, [key]: v } : r));
    setRows(updated);
    setLoading(true);
    try {
      const report = await validateSpreadsheetRows(updated);
      setValidReport(report);
      setWarnings(report.warnings || []);
    } catch (e) {
      console.error(e);
      toast.error("Validation failed while editing");
    } finally {
      setLoading(false);
    }
  };

  const upload = async () => {
    if (!validReport || !allValid) {
      toast.error("Please fix all errors before uploading.");
      return;
    }
    setLoading(true);
    try {
      const res = await uploadValidatedData(validReport);
      if (res.success) {
        toast.success("Upload complete!");
        setRawRows([]);
        setRows([]);
        setValidReport(null);
        setWarnings([]);
        setFileName("");
      } else {
        throw new Error(res.error?.message || "Upload failed");
      }
    } catch (e) {
      console.error(e);
      toast.error(e.message || "Upload failed");
    } finally {
      setLoading(false);
    }
  };

  const renderCell = (row, rowIdx, key) => {
    const value = row[key] ?? "";
    const hasError = !!validReport?.rows?.[rowIdx]?.errors?.some(err => err.field === key);
    return (
      <td key={key}>
        <input
          value={value}
          onChange={(e) => onCellChange(rowIdx, key, e.target.value)}
          className={hasError ? "border border-red-500" : "border border-gray-300"}
          style={{ padding: 10, minWidth: 120 }}
        />
      </td>
    );
  };

  return (
    <div style={{ display: "grid", gap: 12 }}>
      <div className="file-choose">
        <input type="file" accept=".xlsx,.xls,.csv" onChange={handleFile} disabled={loading} />
        {fileName && <small style={{ marginLeft: 8 }}>Loaded: {fileName}</small>}
      </div>

      {warnings?.length > 0 && (
        <div style={{ color: "#fff", background: "#da02027c", padding: 12, borderRadius: 8 }}>
          <strong>Warnings:</strong>
          <ul style={{ marginTop: 6 }}>
            {warnings.map((w, i) => (<li key={i}>• {w}</li>))}
          </ul>
        </div>
      )}

      {headerMap && detectedHeaders?.length > 0 && (
        <div style={{ overflowX: "auto" }}>
            <div className="card" style={{padding:12}}>
              <strong>Review column mapping</strong>
              {[...REQUIRED_COLUMNS, ...OPTIONAL_COLUMNS].map(field => (
                <div key={field} style={{display:"flex", gap:8, alignItems:"center", marginTop:8}}>
                  <span style={{width:140}}>{field}{OPTIONAL_COLUMNS.includes(field) && " (optional)"}</span>
                  <select
                    value={headerMap[field] || ""}
                    onChange={(e)=> setHeaderMap(m => ({...m, [field]: normalizeHeader(e.target.value)}))}
                  >
                    <option value="">— Unmapped —</option>
                    {Array.from(new Set(detectedHeaders.map(normalizeHeader))).map(h => (
                      <option key={h} value={h}>{h}</option>
                    ))}
                  </select>
                </div>
              ))}
              <div style={{marginTop:10, display:"flex", gap:8}}>
                <button className="btn btn-primary" onClick={() => { saveMap(headerMap); toast.success("Mapping saved"); }}>Save mapping</button>
                <button className="btn" onClick={() => { const m = loadMap(); if (m){ setHeaderMap(Object.fromEntries(Object.entries(m).map(([k,v])=>[k, normalizeHeader(v)]))); toast.info("Loaded last mapping"); }}}>Use my last mapping</button>
                <button
                 className="btn"
                 onClick={() => { if (lastSheet) applyMapping(lastSheet); }}
                 disabled={!lastSheet || loading}
               >
                 Apply mapping
               </button>
              </div>
            </div>

          {stage !== "idle" && (
          <table className="min-w-full border-collapse">
            <thead>
              <tr>
                {[...REQUIRED_COLUMNS, ...OPTIONAL_COLUMNS].map(h => (
                  <th key={h} className="text-left border-b p-2">
                    {h}{OPTIONAL_COLUMNS.includes(h) && <span style={{marginLeft:6, fontSize:12, opacity:.7}}>(optional)</span>}
                  </th>
                ))}
                <th className="text-left border-b p-2">Errors</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row, rowIdx) => (
                <tr key={rowIdx}>
                  {[...REQUIRED_COLUMNS, ...OPTIONAL_COLUMNS].map(k => renderCell(row, rowIdx, k))}
                  <td style={{ color: "#dc2626" }}>
                    {validReport?.rows?.[rowIdx]?.errors?.map((e, i) => (
                      <div key={i}>• {e.message}</div>
                    ))}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
            )}

          <div style={{ marginTop: 12, display: "flex", gap: 12, alignItems: "center" }}>
            <button
              onClick={upload}
              disabled={loading || !allValid || stage !== "ready"}
              className="btn btn-primary"
              title={stage !== "ready" ? "Apply mapping and fix any errors before uploading" : ""}
            >
              {loading ? (stage === "ready" ? "Uploading..." : "Validating...") : "Upload"}
            </button>
            {stage !== "ready" && (
              <span style={{ color: "#64748b" }}>Apply mapping and pass validation to enable upload.</span>
            )}
            {stage === "ready" && !allValid && (
              <span style={{ color: "#dc2626" }}>Fix errors to enable upload.</span>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

export default UploadSheets;
