// sheet_converter.jsx

function excelDateToISO(serial) {
  const utcDays = Math.floor(serial - 25569);
  const utcValue = utcDays * 86400; // seconds since 1970-01-01
  const dateInfo = new Date(utcValue * 1000); // convert to milliseconds
  return dateInfo.toISOString(); // e.g., "2025-08-08T00:00:00.000Z"
}

export default excelDateToISO;
