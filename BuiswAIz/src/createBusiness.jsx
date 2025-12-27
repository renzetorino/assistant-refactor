import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { supabase } from './supabase';
import './stylecss/login.css';

const generateBusinessCode = () => {
  const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  let code = 'BWZ-';
  for (let i = 0; i < 6; i++) {
    code += chars.charAt(Math.floor(Math.random() * chars.length));
  }
  return code;
};

const CreateBusiness = () => {
  const navigate = useNavigate();
  const [username, setUsername] = useState('');
  const [businessName, setBusinessName] = useState('');
  const [businessAddress, setBusinessAddress] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleCreate = async (e) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      // 1️⃣ Get current Auth user
      const { data: userData } = await supabase.auth.getUser();
      const userId = userData?.user?.id;

      if (!userId) {
        setError('User not logged in.');
        setLoading(false);
        return;
      }

      // 2️⃣ Update username in existing systemuser
      const { data: systemUser, error: updateError } = await supabase
        .from('systemuser')
        .update({ username })
        .eq('userid', userId)
        .select()
        .maybeSingle(); // <-- safe for multiple rows

      if (updateError) throw updateError;

      // 3️⃣ Create new business_role
      const businessCode = generateBusinessCode();
      const { data: newBusiness, error: businessError } = await supabase
        .from('business_role')
        .insert({
          businessname: businessName,
          businessAddress,
          role: 'owner',
          user_id: userId,
          businesscode: businessCode,
        })
        .select()
        .single();
      if (businessError) throw businessError;

      // 4️⃣ Update systemuser.business_id
      await supabase
        .from('systemuser')
        .update({ business_id: newBusiness.businessid })
        .eq('userid', userId);

      // 5️⃣ Save profile locally and navigate
      localStorage.setItem(
        'userProfile',
        JSON.stringify({
          userid: userId,
          username,
          business_id: newBusiness.businessid,
        })
      );
      localStorage.setItem('lastActive', Date.now());
      navigate('/Dashboard');
    } catch (err) {
      setError(err.message || 'Failed to create business.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-container page-enter-active">
      <div className="login-left">
        <form onSubmit={handleCreate} className="login-form">
          <h2 className="login-title">Create New Business</h2>

          <label className="login-label">Username*</label>
          <input
            className="login-input"
            required
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            placeholder="John Doe"
          />

          <label className="login-label">Business Name*</label>
          <input
            className="login-input"
            required
            value={businessName}
            onChange={(e) => setBusinessName(e.target.value)}
            placeholder="My Business"
          />

          <label className="login-label">Business Address</label>
          <input
            className="login-input"
            value={businessAddress}
            onChange={(e) => setBusinessAddress(e.target.value)}
            placeholder="123 Street, City"
          />

          {error && <p className="error-message">{error}</p>}

          <button className="login-button" type="submit">
            {loading ? 'Creating...' : 'Create Business'}
          </button>
        </form>
      </div>
    </div>
  );
};

export default CreateBusiness;
