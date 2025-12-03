import { useState, useEffect } from "react";
import { supabase } from "../supabase";

export function useBudget() {
  const [budget, setBudget] = useState(0);

  const fetchBudget = async () => {
    const now = new Date();
    const monthYear = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-01`;

    const { data, error } = await supabase
      .from("budget")
      .select("*")
      .eq("month_year", monthYear)
      .single();

    if (!error && data) {
      setBudget(Number(data.monthly_budget_amount));
    } else if (error?.code !== "PGRST116") {
      console.error("Failed to fetch budget:", error);
    }
  };

  const saveBudget = async (amount) => {
    const now = new Date();
    const monthYear = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-01`;

    const { error } = await supabase.from("budget").upsert({
      month_year: monthYear,
      monthly_budget_amount: Number(amount),
    }, { onConflict: ["month_year"] });

    if (error) throw error;
    fetchBudget();
  };

  useEffect(() => {
    fetchBudget();
  }, []);

  return { budget, saveBudget };
}
