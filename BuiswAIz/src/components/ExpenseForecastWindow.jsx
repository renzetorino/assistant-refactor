// src/components/ExpenseForecastWindow.jsx
import React from "react";
import SalesForecastWindow from "./SalesForecastWindow";

/**
 * Wrapper lang to reuse SalesForecastWindow para sa expenses.
 * SalesForecastWindow na mismo ang nagpapalit ng title kapag domain ay "expenses".
 */
export default function ExpenseForecastWindow({ forecast }) {
  // Ensure the domain flag is "expenses" so the title switches correctly
  const data = forecast ? { domain: (forecast.domain || "expenses"), ...forecast } : { domain: "expenses" };
  return <SalesForecastWindow forecast={data} />;
}
