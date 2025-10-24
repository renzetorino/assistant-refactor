// src/pages/Assistant.jsx
import React, { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { supabase } from "../supabase";
import "../stylecss/Assistant.css";
import AssistantChat from "./AssistantChat";
import prompts from "./prompts";

import PopupWindow from "../components/PopupWindow";
import InventoryWindow from "../components/InventoryWindow";
import SalesWindow from "../components/SalesWindow";
import ExpensesWindow from "../components/ExpensesWindow";
// NEW: bring in your forecast popup component
import SalesForecastWindow from "../components/SalesForecastWindow";

const API_BASE = import.meta.env.VITE_API_ASSISTANT_URL

const newId = () =>
  (crypto?.randomUUID ? crypto.randomUUID() : String(Date.now() + Math.random()));

const Assistant = () => {
  const navigate = useNavigate();
  const [user, setUser] = useState(null);

  const [_activeWindow, setActiveWindow] = useState(null);
  const [, setSalesScope] = useState("overall");
  const [, setSelectedProduct] = useState(null);
  const [_lastReportUiSpec, _setLastReportUiSpec] = useState(null);
  const [popupData, setPopupData] = useState(null);

  // Chat state
  const [messages, setMessages] = useState([
    {
      id: newId(),
      role: "assistant",
      text:
        'Hi! Ask me for a sales, inventory, or expense report (e.g., “Create a sales report for September 2025”).',
    },
  ]);
  const [loading, setLoading] = useState(false);
  const [loadingPanels, setLoadingPanels] = useState(true);

  // CENTER panels
  const [reports, setReports] = useState([]);     // up to 2
  const [forecasts, setForecasts] = useState([]); // up to 2; now shown like Spotlight

  // ===== Auth + lock (kept) =====
  useEffect(() => {
    let heartbeat;
    let authUserId;
    let subscription;

    const releaseLock = async () => {
      try {
        if (authUserId) {
          await supabase
            .from("assistant_lock")
            .update({ locked_by: null, locked_at: null })
            .eq("id", 1)
            .eq("locked_by", authUserId);
        }
      } catch (e) {
        console.debug("releaseLock error:", e);
      }
      if (heartbeat) clearInterval(heartbeat);
    };

    const init = async () => {
      const {
        data: { user: authUser },
      } = await supabase.auth.getUser();

      if (!authUser) {
        navigate("/");
        return;
      }
      authUserId = authUser.id;

      const { data: lockAcquired } = await supabase.rpc(
        "acquire_assistant_lock",
        { p_user_id: authUserId }
      );
      if (!lockAcquired) {
        alert("Someone is currently accessing the assistant page.");
        navigate("/inventory");
        return;
      }

      const { data: profile } = await supabase
        .from("systemuser")
        .select("*")
        .eq("userid", authUserId)
        .single();
      setUser(profile || null);

      heartbeat = setInterval(async () => {
        const { data: stillHasLock } = await supabase.rpc(
          "acquire_assistant_lock",
          { p_user_id: authUserId }
        );
        if (!stillHasLock) {
          alert("You lost the lock. Redirecting.");
          clearInterval(heartbeat);
          navigate("/inventory");
        }
      }, 30000);
    };

    init();

    const { data } = supabase.auth.onAuthStateChange((_e, session) => {
      if (!session || session.user.id !== authUserId) releaseLock();
    });
    subscription = data.subscription;

    const onUnload = () => releaseLock();
    window.addEventListener("beforeunload", onUnload);

    return () => {
      releaseLock();
      subscription?.unsubscribe();
      window.removeEventListener("beforeunload", onUnload);
    };
  }, [navigate]);


  // Preload Spotlight - optimized version
  useEffect(() => {
    let alive = true;
    
    const loadData = async () => {
      try {
        // Fetch all domains in parallel for better performance
        const [salesReports, expenseReports, inventoryReports, salesForecasts, expenseForecasts] = await Promise.all([
          fetchRecentReports(2, "sales"),
          fetchRecentReports(2, "expenses"),
          fetchRecentReports(2, "inventory"),
          fetchRecentForecasts(2, "sales"),
          fetchRecentForecasts(2, "expenses"),
        ]);

        if (!alive) return;

        // Merge and take the most recent items (no sorting by created_at since backend may not provide it)
        // Backend should return items in descending order by default
        const allReports = [...salesReports, ...expenseReports, ...inventoryReports].slice(0, 4);
        const allForecasts = [...salesForecasts, ...expenseForecasts].slice(0, 4);

        setReports(allReports);
        setForecasts(allForecasts);
        setLoadingPanels(false);
      } catch (e) {
        console.error("Failed to preload spotlight/forecasts:", e);
        setLoadingPanels(false);
      }
    };

    loadData();
    return () => { alive = false; };
  }, []);

  // ===== API helper =====
  async function apiPost(path, body) {
    const res = await fetch(`${API_BASE}${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body || {}),
    });
    if (!res.ok) {
      throw new Error((await res.text()) || `HTTP ${res.status}`);
    }
    return res.json();
  }

  // ===== Chat send (kept; now also promotes forecast into panel) =====
  const handleSend = async (text) => {
    const trimmed = (text || "").trim();
    if (!trimmed) return;

    setMessages((p) => [...p, { id: newId(), role: "user", text: trimmed }]);
    setLoading(true);

    try {
      const res = await apiPost("/api/assistant", { text: trimmed });

      if (res.mode === "report") {
        // Show simple success message in chat instead of full report payload
        const dom = (res.domain || "report").toLowerCase();
        const reportType = dom === "expenses" ? "Expense" : dom === "inventory" ? "Inventory" : "Sales";
        const successMessage = `✓ ${reportType} report has been successfully created! You can view the full report in the panel above.`;
        
        setMessages((prev) => [
          ...prev,
          { id: newId(), role: "assistant", text: successMessage }
        ]);

        // 1) push a placeholder card (fast UI), but include the domain
        const placeholder = normalizeReportForSpotlightRow({ ui_spec: res.uiSpec, domain: dom });
        if (placeholder) setReports((prev) => [placeholder, ...prev].slice(0, 2));

        // 2) then fetch the latest saved run for that domain to get the real runId
        try {
          const latest = await fetchRecentReports(1, dom);
          if (Array.isArray(latest) && latest.length > 0) {
            // replace the first card with the saved one that has runId
            setReports((prev) => [latest[0], ...prev.slice(1)]);
          }
        } catch { /* ignore */ }

        return;
      }

      if (res.mode === "faq") {
        const md = res.uiSpec?.render?.content || "No answer available.";
        const actions = Array.isArray(res.uiSpec?.suggestedActions)
          ? res.uiSpec.suggestedActions.map((a) => ({ id: a.id || a.label, label: a.label }))
          : null;
        setMessages((p) => [...p, { id: newId(), role: "assistant", text: md, actions }]);
        return;
      }

      if (res.mode === "chitchat") {
        const md = res.uiSpec?.render?.content || "…";
        const actions = Array.isArray(res.uiSpec?.suggestedActions)
          ? res.uiSpec.suggestedActions.map((a) => ({ id: a.id || a.label, label: a.label }))
          : null;
        setMessages((p) => [...p, { id: newId(), role: "assistant", text: md, actions }]);
        return;
      }

      if (res.mode === "forecast") {
        // 1) summary lines for the chat bubble (existing behavior)
        const f = res.uiSpec ?? {};
        const domain = (res.domain || "sales").toLowerCase();
        const title = domain === "expenses" ? "Expense Forecast" : "Sales Forecast";

        const lines = [
          `${title} — ${f?.period?.label || ""}`.trim(),
        ];

        if (typeof f?.notes?.narrative === "string" && f.notes.narrative.trim()) {
          lines.push("", f.notes.narrative.trim());
        }

        setMessages((prev) => [
          ...prev,
          { id: newId(), role: "assistant", text: lines.join("\n") },
        ]);

        // 2) also promote to the CENTER "Forecasts" panel as a card
        //    (so expenses show up there too)
        const card = normalizeForecastForCard(
          { ...f, domain }, // ensure domain is carried
          domain
        );
        setForecasts((prev) => [card, ...prev].slice(0, 2));

        return;
      }

      if (res.mode === "nlq") {
        const md = res.uiSpec?.render?.content || res.notice || "Here's your data.";
        setMessages((p) => [...p, { id: newId(), role: "assistant", text: md }]);
        return;
      }

      setMessages((p) => [...p, { id: newId(), role: "assistant", text: "Hi! How can I help?" }]);
    } catch (err) {
      console.error(err);
      setMessages((p) => [
        ...p,
        { id: newId(), role: "assistant", text: "Sorry, I couldn’t process that.\n\n" + (err?.message || err) },
      ]);
    } finally {
      setLoading(false);
    }
  };

  const handleAction = async (actionId, message) => {
    const id = (actionId || "").toLowerCase();
    if (id === "regenerate" || id === "regenerate_report") {
      const idx = messages.findIndex((m) => m.id === message?.id);
      const lastUser = [...messages].slice(0, idx).reverse().find((m) => m.role === "user");
      if (lastUser?.text) await handleSend(lastUser.text);
      return;
    }
    if (id === "download" || id === "download_pdf") {
      alert("Download PDF is coming soon.");
      return;
    }
  };

  // ===== Recent fetchers (optimized with better error handling) =====
  async function fetchRecentReports(limit = 2, domain = "sales") {
    try {
      const res = await fetch(`${API_BASE}/api/reports/recent?domain=${encodeURIComponent(domain)}&limit=${limit}`);
      if (!res.ok) {
        console.warn(`Failed to fetch reports for ${domain}:`, res.status);
        return [];
      }
      const data = await res.json();
      return Array.isArray(data) ? data.map(normalizeReportForSpotlightRow).filter(Boolean) : [];
    } catch (error) {
      console.error(`Error fetching reports for ${domain}:`, error);
      return [];
    }
  }

  async function fetchRecentForecasts(limit = 2, domain = "sales") {
    try {
      const res = await fetch(
        `${API_BASE}/api/forecasts/recent?domain=${encodeURIComponent(domain)}&limit=${limit}`
      );
      if (!res.ok) {
        console.warn(`Failed to fetch forecasts for ${domain}:`, res.status);
        return [];
      }
      const data = await res.json();
      
      if (!Array.isArray(data)) return [];

      return data.map((x) => {
        const dom = (x?.domain || domain || "sales").toLowerCase();
        const ui = {
          period: {
            start: x?.params?.start,
            end: x?.params?.end,
            label: x?.params?.label,
          },
          kpis: x?.result?.kpis ?? {},
          series: x?.result?.series ?? {},  // ✅ INCLUDE SERIES DATA FOR CHART!
          notes: x?.result?.ui_spec?.notes ?? x?.result?.notes ?? null,
          domain: dom,
        };

        return normalizeForecastForCard(ui, dom);
      }).filter(Boolean);
    } catch (error) {
      console.error(`Error fetching forecasts for ${domain}:`, error);
      return [];
    }
  }

  // ===== Open handlers =====
  function openSpotlightCard(card) {
    if (!card) return;

    const rawDomain = (card.domain || "report").toLowerCase();
    const isExpenses  = /expense/.test(rawDomain);    // "expense" or "expenses"
    const isInventory = /invent/.test(rawDomain);

    const scope   = (card.scope  || "overall").toLowerCase();
    const runId   = card.runId || null;
    const product = card.product || null;

    // keep your sales-only UI state
    if (!isExpenses && !isInventory) {
      setSalesScope(scope);
      setSelectedProduct(product || null);
    }

    setPopupData({
      type: isExpenses
        ? "report-expenses"
        : isInventory
        ? "report-inventory"
        : "report-sales",
      domain: isExpenses ? "expenses" : isInventory ? "inventory" : "sales",
      runId,
      scope,
      product
    });
  }

  // NEW: open forecast card in popup with full details
  function openForecastCard(card) {
  if (!card) return;
    const dom = (card.domain || "sales").toLowerCase();

    setPopupData({
      type: "forecast",
      domain: dom,
      // include domain inside ui so the window is domain-aware
      ui: card.ui_spec ? { domain: dom, ...card.ui_spec } : { domain: dom },
      title: card.title || "Forecast",
    });

    // optional: if you want the "Forecasts" tab active
    // setActiveWindow("forecast");
  }

  return (
    <div className="assistant-page">
      <header className="header-bar">
        <h1 className="header-title">BuiswAIz</h1>
      </header>

      <div className="main-section">
        {/* LEFT: Sidebar */}
        <aside className="sidebar">
          <div className="nav-section">
            <p className="nav-header">GENERAL</p>
            <ul>
              <li onClick={() => navigate("/Dashboard")}>Dashboard</li>
              <li onClick={() => navigate("/inventory")}>Inventory</li>
              <li onClick={() => navigate("/TablePage")}>Sales</li>
              <li onClick={() => navigate("/expenses")}>Expenses</li>
              <li className="active">AI Assistant</li>
            </ul>

            <p className="nav-header">RELATED</p>
            <ul>
              <li onClick={() => navigate("/supplier")}>Supplier</li>
              <li onClick={() => navigate("/pos")}>Point of Sales</li>
              <li onClick={() => navigate("/PlannedPaymentsPage")}>Planned Payment</li>
            </ul>
          </div>
        </aside>

        {/* 2 columns: CENTER (reports/forecast) + RIGHT (chat) */}
        <div className="A-body">
          {/* CENTER column */}
          <div className="A-center">
            {/* TOP: ReportSpotlight (unchanged) */}
            <section className="report-spotlight">
              <div className="section-title">ReportSpotlight</div>
              {loadingPanels ? (
                <div className="report-empty">Loading reports...</div>
              ) : reports.length === 0 ? (
                <div className="report-empty">
                  No reports yet. Ask me for a report to populate this section.
                </div>
              ) : (
                <div className="report-grid">
                  {reports.map((r, idx) => (
                    <article key={r.id || idx} className="report-card">
                      <div className="report-card-header">
                        <h3 className="report-title">{r.title}</h3>
                        {r.period_label && <div className="report-chip">{r.period_label}</div>}
                      </div>

                      <div className="report-actions">
                        <button className="btn-soft" onClick={() => openSpotlightCard(r)}>
                          Open
                        </button>
                      </div>
                    </article>
                  ))}
                </div>
              )}
            </section>

            {/* BOTTOM: Forecasts — now same minimal card UI as Spotlight */}
            <section className="forecast-panel">
              <div className="section-title">Forecasts</div>
              {loadingPanels ? (
                <div className="forecast-empty">Loading forecasts...</div>
              ) : forecasts.length === 0 ? (
                <div className="forecast-empty">Independent forecasts will appear here.</div>
              ) : (
                <div className="report-grid">{/* reuse same grid styles */}
                  {forecasts.map((f, idx) => (
                    <article key={f.id || idx} className="report-card">{/* reuse same card styles */}
                      <div className="report-card-header">
                        <h3 className="report-title">{f.title}</h3>
                        {f.period_label && <div className="report-chip">{f.period_label}</div>}
                      </div>
                      <div className="report-actions">
                        <button className="btn-soft" onClick={() => openForecastCard(f)}>
                          Open
                        </button>
                      </div>
                    </article>
                  ))}
                </div>
              )}
            </section>
          </div>

          {/* RIGHT: Chat column */}
          <div className="A-chat-column">
            <div className="A-user-info-card">
              <div className="A-user-left">
                <div className="A-user-avatar" />
                <div className="A-user-username">
                  {user ? user.username || user.email : "Loading."}
                </div>
              </div>
               
            </div>
            <AssistantChat
              messages={messages}
              loading={loading}
              onSend={handleSend}
              onAction={handleAction}
              prompts={prompts}
            />
          </div>
        </div>
      </div>

      {/* Pop-ups */}
      <PopupWindow
        open={!!popupData}
        title={
          popupData?.type === "forecast"
            ? (popupData?.title ||
              (/expense/i.test(popupData?.domain || "") ? "Expense Forecast" : "Sales Forecast"))
            : popupData?.type === "report-sales"
            ? (popupData?.scope === "item"
                ? `AI Sales Report — ${popupData?.product?.name || "Pick a product"}`
                : "AI Sales Report")
            : popupData?.type === "report-expenses"
            ? "AI Expense Summary Report"
            : popupData?.type === "report-inventory"
            ? "AI Inventory Insight"
            : "AI Report"
        }
        onClose={() => { setPopupData(null); setActiveWindow(null); }}
      >
        {/* Sales Report */}
        {popupData?.type === "report-sales" && (
          <SalesWindow
            scope={popupData?.scope || "overall"}
            product={popupData?.product || null}
            runId={popupData?.runId || null}
          />
        )}

        {/* Expense Report */}
        {popupData?.type === "report-expenses" && (
          <ExpensesWindow
            // IMPORTANT: para hindi mag-"latest" lagi — gagamitin ang eksaktong run
            runId={popupData?.runId || null}
          />
        )}

        {/* Inventory */}
        {popupData?.type === "report-inventory" && (
          <InventoryWindow />
        )}

        {/* Forecasts */}
        {popupData?.type === "forecast" && (
          <SalesForecastWindow
            // domain-aware ang forecast window (Sales/Expense title sa loob)
            forecast={
              popupData?.ui
                ? { domain: (popupData?.domain || "sales"), ...popupData.ui }
                : { domain: (popupData?.domain || "sales") }
            }
          />
        )}
      </PopupWindow>
    </div>
  );
};

export default Assistant;

/* ===== Helpers (optimized) ===== */
function normalizeReportForSpotlightRow(row) {
  if (!row) return null;
  
  const ui = row?.ui_spec ?? row?.ui ?? row ?? {};

  // Prefer explicit domain -> ui.domain -> guess-by-title -> default
  const rawDom = (row?.domain ?? ui?.domain ?? guessDomainFromTitle(ui?.report_title) ?? "report").toString().toLowerCase();
  const domain =
    /expense/.test(rawDom) ? "expenses" :
    /invent/.test(rawDom)  ? "inventory" :
    /sale/.test(rawDom)    ? "sales" :
    "report";

  const scope = String(ui?.scope ?? "overall").toLowerCase();
  const productName = (ui?.product?.name || ui?.product_name || ui?.item_name || "").toString().trim();
  const periodLabel = ui?.period?.label || buildPeriodLabel(ui?.period?.start, ui?.period?.end) || "Period";

  const title =
    scope === "item" && productName
      ? `${capitalize(domain)} Report — ${productName} (${periodLabel})`
      : `${capitalize(domain)} Report — ${periodLabel}`;

  const runId = row?.run_id ?? row?.id ?? null;

  return {
    id: newId(),
    domain,
    scope,
    product: productName ? { id: null, name: productName } : null,
    period_label: periodLabel,
    title,
    runId,
  };
}

function normalizeForecastForCard(ui, domain = "sales") {
  if (!ui) return null;
  
  const periodLabel = ui?.period?.label || buildPeriodLabel(ui?.period?.start, ui?.period?.end) || "Period";
  const title = /expense/i.test(domain) ? "Expense Forecast" : "Sales Forecast";

  return {
    id: newId(),
    domain: /expense/i.test(domain) ? "expenses" : "sales",
    title,
    period_label: periodLabel,
    ui_spec: ui,
  };
}

function buildPeriodLabel(start, end) {
  if (!start || !end) return null;
  try {
    const s = new Date(start), e = new Date(end);
    const sameMonth = s.getUTCFullYear() === e.getUTCFullYear() && s.getUTCMonth() === e.getUTCMonth();
    const fmt = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric", timeZone: "UTC" });
    const fmtMo = new Intl.DateTimeFormat("en-US", { month: "short", year: "numeric", timeZone: "UTC" });
    if (sameMonth && s.getUTCDate() === 1) {
      return fmtMo.format(s); // "Oct 2025"
    }
    const sameYear = s.getUTCFullYear() === e.getUTCFullYear();
    const left = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", timeZone: "UTC" }).format(s);
    const right = sameYear
      ? new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric", timeZone: "UTC" }).format(e)
      : fmt.format(e);
    return `${left}–${right}`; // "Oct 1–Oct 31, 2025"
  } catch {
    return null;
  }
}

function guessDomainFromTitle(t) {
  const s = String(t || "").toLowerCase();
  if (s.includes("sale")) return "sales";
  if (s.includes("expense")) return "expenses";
  if (s.includes("invent")) return "inventory";
  return "report";
}
function capitalize(s) { return (s || "").charAt(0).toUpperCase() + (s || "").slice(1); }
