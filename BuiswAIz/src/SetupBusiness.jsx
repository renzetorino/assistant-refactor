// src/SetupBusiness.jsx
import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { supabase } from './supabase';
import './stylecss/login.css';

const SetupBusiness = () => {
  const navigate = useNavigate();
  const [userId, setUserId] = useState(null);
  const [error, setError] = useState('');

  // Fetch the logged-in user
  useEffect(() => {
    const fetchUser = async () => {
      const { data, error: authError } = await supabase.auth.getUser();

      if (authError || !data?.user) {
        navigate('/login'); // redirect if not logged in
      } else {
        setUserId(data.user.id);
      }
    };

    fetchUser();
  }, [navigate]);

  // Handlers for Create / Join business
  const handleCreate = () => {
    if (!userId) return setError('User not found.');
    navigate('/create-business');
  };

  const handleJoin = () => {
    if (!userId) return setError('User not found.');
    navigate('/join-business');
  };

  return (
    <div className="login-container page-enter-active">
      <div className="login-left">
        <div className="login-form">
          <h2>Welcome! Let's set up your business.</h2>
          <p>Please choose an option:</p>

          {error && <p className="error-message">{error}</p>}

          <button type="button" className="login-button" onClick={handleCreate}>
            Create New Business
          </button>
          <button type="button" className="login-button" onClick={handleJoin}>
            Join Existing Business
          </button>
        </div>
      </div>
    </div>
  );
};

export default SetupBusiness;
