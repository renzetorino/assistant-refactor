export default function BudgetFormModal({ show, onClose, onSave, value, setValue }) {
  if (!show) return null;
  return (
    <div className="modal-overlay">
      <form className="modal" onSubmit={(e) => { e.preventDefault(); onSave(); }}>
        <h2>Add Monthly Budget</h2>
        <input type="number" placeholder="Enter budget amount" value={value} onChange={(e) => setValue(e.target.value)} />
        <div className="modal-actions">
          <button type="button" onClick={onClose}>Cancel</button>
          <button type="submit">Save Budget</button>
        </div>
      </form>
    </div>
  );
}
