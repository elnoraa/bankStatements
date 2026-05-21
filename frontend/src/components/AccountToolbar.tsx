import type { RefObject } from 'react';
import type { BankAccount } from '../types';
import { TOTAL_ID } from '../types';

interface AccountToolbarProps {
  accounts: BankAccount[];
  selectedAccountId: string;
  setSelectedAccountId: (id: string) => void;
  editingAccountId: string | null;
  editingAccountName: string;
  setEditingAccountName: (name: string) => void;
  editInputRef: RefObject<HTMLInputElement | null>;
  handleStartRename: (account: BankAccount) => void;
  handleSaveRename: (accountId: string) => Promise<void>;
  cancelRename: () => void;
  handleDeleteAccount: (accountId: string) => Promise<void>;
  handleAddAccount: () => Promise<void>;
  isAccountsLoading: boolean;
  accountsMessage: string;
}

export function AccountToolbar({
  accounts, selectedAccountId, setSelectedAccountId,
  editingAccountId, editingAccountName, setEditingAccountName,
  editInputRef, handleStartRename, handleSaveRename, cancelRename,
  handleDeleteAccount, handleAddAccount, isAccountsLoading, accountsMessage,
}: AccountToolbarProps) {
  return (
    <div className="account-bar">
      <label className="account-bar-label">Account:</label>
      <div className="account-select-wrapper">
        <select
          className="account-select"
          value={selectedAccountId}
          onChange={(e) => setSelectedAccountId(e.target.value)}
        >
          <option value={TOTAL_ID}>Total (all accounts)</option>
          {accounts.length > 0 && <option disabled>──────────</option>}
          {accounts.map((account) => (
            <option key={account.id} value={account.id}>
              {account.accountName}
            </option>
          ))}
        </select>
      </div>
      <div className="account-list">
        {accounts.map((account) => (
          <div className="account-item" key={account.id}>
            {editingAccountId === account.id ? (
              <input
                ref={editInputRef}
                className="account-name-edit"
                value={editingAccountName}
                onChange={(e) => setEditingAccountName(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') void handleSaveRename(account.id);
                  if (e.key === 'Escape') cancelRename();
                }}
                onBlur={() => void handleSaveRename(account.id)}
                maxLength={120}
              />
            ) : (
              <>
                <span
                  className="account-name-clickable"
                  onClick={() => handleStartRename(account)}
                  title="Click to rename"
                >
                  {account.accountName}
                </span>
                <div className="account-actions">
                  <button
                    className="account-action-btn"
                    type="button"
                    title="Rename"
                    onClick={() => handleStartRename(account)}
                  >
                    ✎
                  </button>
                  <button
                    className="account-action-btn account-action-delete"
                    type="button"
                    title="Delete account and all its statements"
                    onClick={() => {
                      if (window.confirm(`Delete "${account.accountName}" and all its statements?`)) {
                        void handleDeleteAccount(account.id);
                      }
                    }}
                  >
                    ×
                  </button>
                </div>
              </>
            )}
          </div>
        ))}
      </div>
      <button
        className="account-add-btn"
        type="button"
        onClick={() => void handleAddAccount()}
        disabled={isAccountsLoading}
        title="Add account"
      >
        + Add account
      </button>
      {accountsMessage && <p className="error-text account-message">{accountsMessage}</p>}
    </div>
  );
}
