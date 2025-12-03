import React from "react";
import Calendar from "react-calendar";
import "react-calendar/dist/Calendar.css";

export default function CalendarPanel({ date, onChange }) {
  return (
    <div className="calendar-container">
      <h3>Calendar</h3>
      <Calendar value={date} onChange={onChange} />
    </div>
  );
}
