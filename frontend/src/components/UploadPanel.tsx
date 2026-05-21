import { type FormEvent } from 'react';
import type { StatementUploadResponse } from '../types';

interface UploadPanelProps {
  selectedAccountId: string;
  selectedFile: File | null;
  setSelectedFile: (file: File | null) => void;
  isUploadLoading: boolean;
  handleUpload: (event: FormEvent<HTMLFormElement>) => Promise<void>;
  upload: StatementUploadResponse | null;
  statementStatus: string | null;
  parsedTransactionCount: number;
  appMessage: string;
  selectedAccountName: string | null;
}

export function UploadPanel({
  selectedAccountId, selectedFile, setSelectedFile,
  isUploadLoading, handleUpload, upload,
  statementStatus, parsedTransactionCount, appMessage,
  selectedAccountName,
}: UploadPanelProps) {
  return (
    <form className="panel upload-panel" onSubmit={handleUpload}>
      <div>
        <p className="panel-label">PDF upload</p>
        <h2>Parse a bank statement</h2>
      </div>

      {selectedAccountId === '__total__' ? (
        <p className="empty-state upload-hint">
          Select a specific account above to upload a statement.
        </p>
      ) : (
        <>
          <p className="upload-context">
            Uploading to: <strong>{selectedAccountName ?? 'Unknown'}</strong>
          </p>
          <label className="file-input">
            <span>{selectedFile ? selectedFile.name : 'Choose a PDF statement'}</span>
            <input
              type="file"
              accept="application/pdf,.pdf"
              onChange={(event) => setSelectedFile(event.target.files?.[0] ?? null)}
            />
          </label>
          <button
            className="primary-button"
            type="submit"
            disabled={!selectedFile || isUploadLoading}
          >
            {isUploadLoading ? 'Uploading...' : 'Upload and analyse'}
          </button>
        </>
      )}

      {upload && (
        <p className={statementStatus === 'failed' ? 'error-text' : 'success-text'}>
          {statementStatus === 'uploaded' && `${upload.originalFileName} uploaded — processing...`}
          {statementStatus === 'processing' && `${upload.originalFileName} processing...`}
          {statementStatus === 'processed' && `${upload.originalFileName} processed with ${parsedTransactionCount} transactions.`}
          {statementStatus === 'failed' && `${upload.originalFileName} processing failed.`}
        </p>
      )}
      {appMessage && <p className="error-text">{appMessage}</p>}
    </form>
  );
}
