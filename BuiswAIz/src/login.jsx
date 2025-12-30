// src/Login.jsx
import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { supabase } from './supabase';
import rightImage from './assets/rightimage.jpg';
import './stylecss/login.css';

const Login = () => {
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
  const [error, setError] = useState('');

  // ---- CHECK IF USER IS ALREADY LOGGED IN ----
  useEffect(() => {
    const checkLoggedIn = async () => {
      const { data } = await supabase.auth.getSession();
      if (!data?.session) return;

      const userId = data.session.user.id;

      const { data: userProfile } = await supabase
        .from('systemuser')
        .select('*')
        .eq('userid', userId)
        .maybeSingle();

      if (!userProfile || userProfile.username == null || userProfile.business_id == null) {
        navigate('/setup-business');
      } else {
        localStorage.setItem('lastActive', Date.now());
        if (localStorage.getItem('rememberMe') === 'true') {
          localStorage.setItem('userProfile', JSON.stringify(userProfile));
        }
        navigate('/Dashboard');
      }
    };

    checkLoggedIn();
  }, [navigate]);

  // ---- RESET TIMER ON USER ACTIVITY ----
  useEffect(() => {
    const resetTimer = () => localStorage.setItem('lastActive', Date.now());

    window.addEventListener('click', resetTimer);
    window.addEventListener('keydown', resetTimer);

    return () => {
      window.removeEventListener('click', resetTimer);
      window.removeEventListener('keydown', resetTimer);
    };
  }, []);

  // ---- AUTO-LOGOUT AFTER 5 MINUTES ----
  useEffect(() => {
    const interval = setInterval(async () => {
      const lastActive = localStorage.getItem('lastActive');
      const fiveMinutes = 5 * 60 * 1000;

      if (lastActive && Date.now() - parseInt(lastActive) > fiveMinutes) {
        await supabase.auth.signOut();
        localStorage.removeItem('userProfile');
        localStorage.removeItem('lastActive');
        localStorage.removeItem('rememberMe');
        navigate('/login');
      }
    }, 30 * 1000); // check every 30 seconds

    return () => clearInterval(interval);
  }, [navigate]);

  // ---- LOGIN HANDLER ----
  const handleLogin = async (e) => {
    e.preventDefault();
    setError('');

    try {
      const { data: authData, error: authError } = await supabase.auth.signInWithPassword({
        email,
        password,
      });

      if (authError || !authData?.user) {
        setError('Invalid email or password.');
        return;
      }

      if (!authData.user.confirmed_at) {
        setError('Please verify your email before logging in.');
        return;
      }

      const userId = authData.user.id;

      const { data: userProfile, error: profileError } = await supabase
        .from('systemuser')
        .select('*')
        .eq('userid', userId)
        .maybeSingle();

      if (profileError) {
        setError('An error occurred fetching your profile.');
        return;
      }

      // redirect based on profile
      if (!userProfile?.username || !userProfile?.business_id) {
        navigate('/setup-business');
      } else {
        navigate('/Dashboard');
      }

    } catch (err) {
      console.error(err);
      setError('An unexpected error occurred.');
    }
  };


  return (
    <div className="login-container page-enter-active">
      {/* LEFT SIDE */}
      <div className="login-left">
        <form onSubmit={handleLogin} className="login-form">
          <h2 className="login-title">Sign In</h2>
          <p className="login-subtitle">Enter your email and password to sign in!</p>

          <label className="login-label">Email*</label>
          <input
            type="email"
            required
            placeholder="mail@simmmple.com"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className="login-input"
          />

          <label className="login-label">Password*</label>
          <input
            type="password"
            required
            placeholder="Min. 8 characters"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="login-input"
          />

          <div className="login-options">
            <label className="checkbox-label">
              <input
                type="checkbox"
                checked={rememberMe}
                onChange={() => setRememberMe(!rememberMe)}
              />
              Keep me logged in
            </label>

            <span className="forgot-password" onClick={() => navigate('/forgot-password')}>
              Forgot password?
            </span>
          </div>

          {error && <p className="error-message">{error}</p>}

          <button type="submit" className="login-button">
            Sign In
          </button>

          <button type="button" className="signup-button" onClick={() => navigate('/signup')}>
            Sign Up
          </button>
        </form>
      </div>

      {/* RIGHT SIDE */}
      <div className="login-right">
        <div className="login-logo-container">
          <img src={rightImage} alt="BuisWaiz Logo" className="login-logo-image" />
        </div>
      </div>
    </div>
  );
};

export default Login;
