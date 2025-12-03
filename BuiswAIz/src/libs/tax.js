// src/lib/tax.js
export function calcTax({ amount, type, rate, isInclusive, withholdingRate = 0 }) {
  const a = Number(amount) || 0;
  const r = Number(rate) || 0;
  const w = Number(withholdingRate) || 0;
  const clamp = (n) => (Number.isFinite(n) ? n : 0);

  if (type === 'NONE' || r <= 0) {
    const gross = clamp(a);
    const net = gross;
    const tax = 0;
    const withholding = clamp(net * w);
    return { net, tax, gross, withholding };
  }

  if (isInclusive) {
    const gross = clamp(a);
    const net = clamp(gross / (1 + r));
    const tax = clamp(gross - net);
    const withholding = clamp(net * w);
    return { net, tax, gross, withholding };
  } else {
    const net = clamp(a);
    const tax = clamp(net * r);
    const gross = clamp(net + tax);
    const withholding = clamp(net * w);
    return { net, tax, gross, withholding };
  }
}
