// src/NewBusinessSignup.jsx
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { supabase } from './supabase';
import rightImage from './assets/rightimage.jpg';
import './stylecss/login.css';

const NewBusinessSignup = () => {
  const navigate = useNavigate();
  const [form, setForm] = useState({ email: '', password: '' });
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError('');
    setMessage('');
    setLoading(true);

    try {
      const { data, error } = await supabase.auth.signUp(
        {
          email: form.email,
          password: form.password,
        },
        {
          emailRedirectTo: `${import.meta.env.VITE_FE_URL}/create-business`,
        }
      );

      if (error) {
        setError(error.message);
        return;
      }

      setMessage(
        'Signup successful! Please check your email to verify your account before proceeding.'
      );
    } catch (err) {
      console.error(err);
      setError('An unexpected error occurred.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-container page-enter-active">
      <div className="login-left">
        <form className="login-form" onSubmit={handleSubmit}>
          <h2 className="login-title">Sign Up</h2>
          <p className="login-subtitle">Create your account to get started.</p>

          <label className="login-label">Email*</label>
          <input
            type="email"
            placeholder="mail@example.com"
            required
            className="login-input"
            onChange={(e) => setForm({ ...form, email: e.target.value })}
          />

          <label className="login-label">Password*</label>
          <input
            type="password"
            placeholder="Min. 6 characters"
            required
            className="login-input"
            onChange={(e) => setForm({ ...form, password: e.target.value })}
          />

          {error && <p className="error-message">{error}</p>}
          {message && <p className="success-message">{message}</p>}

          <button className="login-button" disabled={loading}>
            {loading ? 'Signing up...' : 'Sign Up'}
          </button>

          <p style={{ marginTop: '16px', textAlign: 'center' }}>
            Already have an account?{' '}
            <span
              style={{ color: '#4f46e5', cursor: 'pointer' }}
              onClick={() => navigate('/login')}
            >
              Sign In
            </span>
          </p>
        </form>
      </div>

      <div className="login-right">
        <div className="login-logo-container">
          <img src={rightImage} alt="BuisWaiz Logo" className="login-logo-image" />
        </div>
      </div>
    </div>
  );
};

export default NewBusinessSignup;
