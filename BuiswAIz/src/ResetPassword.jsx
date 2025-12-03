import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { supabase } from './supabase';
import './stylecss/login.css';

const ResetPassword = () => {
  const navigate = useNavigate();
  const [newPassword, setNewPassword] = useState('');
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  const handleUpdatePassword = async (e) => {
    e.preventDefault();
    setError('');
    setMessage('');

    const { data, error } = await supabase.auth.updateUser({
      password: newPassword,
    });

    if (error) {
      setError(error.message);
    } else {
      setMessage('Password has been updated!');
      setTimeout(() => navigate('/'), 2000);
    }
  };

  return (
    <div className="login-container">
      <div className="login-left">
        <form onSubmit={handleUpdatePassword} className="login-form">
          <h2 className="login-title">Set New Password</h2>

          <label className="login-label">New Password*</label>
          <input
            type="password"
            required
            placeholder="Min. 8 characters"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            className="login-input"
          />

          {message && <p className="success-message">{message}</p>}
          {error && <p className="error-message">{error}</p>}

          <button type="submit" className="login-button">Update Password</button>
        </form>
      </div>
      
      <div className="login-right">
        <h1 className="login-logo">BuisWaiz</h1>
      </div>
    </div>
  );
};

export default ResetPassword;
