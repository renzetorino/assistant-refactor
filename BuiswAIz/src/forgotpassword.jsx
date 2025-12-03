import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { supabase } from './supabase';
import rightImage from './assets/rightimage.jpg';
import './stylecss/login.css';

const ForgotPassword = () => {
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  const handleReset = async (e) => {
    e.preventDefault();
    setMessage('');
    setError('');

    const { error } = await supabase.auth.resetPasswordForEmail(email, {
      redirectTo: `${import.meta.env.VITE_FE_URL}/reset-password`,
    });

    if (error) {
      setError(error.message);
    } else {
      setMessage('Password reset email sent. Check your inbox.');
    }
  };

  return (
    <div className="login-container page-enter-active">
      <div className="login-left">
        <form onSubmit={handleReset} className="login-form">
          <div className="back-button-container">
            <button 
              type="button" 
              onClick={() => navigate('/login')} 
              className="back-button"
            >
              ← Back to Sign In
            </button>
          </div>
          
          <h2 className="login-title">Reset Password</h2>
          <p className="login-subtitle">Enter your email address to receive a password reset link!</p>

          <label className="login-label">Email*</label>
          <input
            type="email"
            required
            placeholder="Enter your email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className="login-input"
          />

          {message && <p className="success-message">{message}</p>}
          {error && <p className="error-message">{error}</p>}

          <button type="submit" className="login-button">Send Reset Email</button>
        </form>
      </div>

      <div className="login-right">
        <div className="login-logo-container">
          <img 
            src={rightImage} 
            alt="BuisWaiz Logo" 
            className="login-logo-image"
          />
        </div>
      </div>
    </div>
  );
};

export default ForgotPassword;