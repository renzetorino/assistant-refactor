// src/JoinBusiness.jsx
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { supabase } from './supabase';
import './stylecss/login.css';

const JoinBusiness = () => {
  const navigate = useNavigate();
  const [username, setUsername] = useState('');
  const [businessCode, setBusinessCode] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleJoin = async (e) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      // 1️⃣ Get logged-in user
      const { data: authData, error: authErr } = await supabase.auth.getUser();
      if (authErr || !authData?.user) {
        throw new Error('User not authenticated');
      }

      const userId = authData.user.id;
      const email = authData.user.email;

      // 2️⃣ Find existing business by code
      const { data: business, error: bizErr } = await supabase
        .from('business_role')
        .select('businessid')
        .eq('businesscode', businessCode)
        .single();

      if (bizErr || !business) {
        throw new Error('Invalid business code');
      }

      // 3️⃣ Update EXISTING systemuser
      const { error: updateErr } = await supabase
        .from('systemuser')
        .update({
          username,
          email,
          business_id: business.businessid,
        })
        .eq('userid', userId);

      if (updateErr) throw updateErr;

      // 4️⃣ Save session
      localStorage.setItem(
        'userProfile',
        JSON.stringify({
          userid: userId,
          username,
          email,
          business_id: business.businessid,
        })
      );

      localStorage.setItem('lastActive', Date.now());
      navigate('/Dashboard');
    } catch (err) {
      console.error(err);
      setError(err.message || 'Failed to join business');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-container page-enter-active">
      <div className="login-left">
        <form className="login-form" onSubmit={handleJoin}>
          <h2 className="login-title">Join Existing Business</h2>

          <label className="login-label">Username*</label>
          <input
            className="login-input"
            required
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            placeholder="Your username"
          />

          <label className="login-label">Business Code*</label>
          <input
            className="login-input"
            required
            value={businessCode}
            onChange={(e) => setBusinessCode(e.target.value)}
            placeholder="BWZ-XXXXXX"
          />

          {error && <p className="error-message">{error}</p>}

          <button className="login-button" type="submit">
            {loading ? 'Joining...' : 'Join Business'}
          </button>
        </form>
      </div>
    </div>
  );
};

export default JoinBusiness;
