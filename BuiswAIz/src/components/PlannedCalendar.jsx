import React, { useMemo } from "react";
import Calendar from "react-calendar";
import "react-calendar/dist/Calendar.css";
import { format, isSameDay, parseISO } from "date-fns";

export default function PlannedCalendar({ payments, onSelectDate }) {
  const tileContent = ({ date, view }) => {
    if (view !== "month") return null;
    const hasDue = payments.some(pp => isSameDay(parseISO(pp.due_date), date));
    return hasDue ? <div className="due-dot" /> : null;
  };

  const tileClassName = ({ date }) => {
    const hasDue = payments.some(pp => isSameDay(parseISO(pp.due_date), date));
    return hasDue ? "calendar-has-due" : "";
  };

  return (
    <div className="calendar-wrapper">
      <Calendar
        onClickDay={onSelectDate}
        tileContent={tileContent}
        tileClassName={tileClassName}
      />
      <style jsx>{`
        .calendar-has-due {
          background: #e0f2fe;
          border-radius: 6px;
        }
        .due-dot {
          width: 6px;
          height: 6px;
          background: #0284c7;
          border-radius: 50%;
          margin: 2px auto 0;
        }
      `}</style>
    </div>
  );
}
