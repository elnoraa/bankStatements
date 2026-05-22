interface DateRangePickerProps {
  dateFrom: string | null;
  dateTo: string | null;
  onDateFromChange: (date: string | null) => void;
  onDateToChange: (date: string | null) => void;
}

const PRESETS = [
  { label: 'All time', days: 0 },
  { label: 'Last 30 days', days: 30 },
  { label: 'This quarter', days: 90 },
  { label: 'This year', days: 365 },
] as const;

export function DateRangePicker({ dateFrom, dateTo, onDateFromChange, onDateToChange }: DateRangePickerProps) {
  function handlePreset(days: number) {
    if (days === 0) {
      onDateFromChange(null);
      onDateToChange(null);
      return;
    }
    const to = new Date();
    const from = new Date();
    from.setDate(from.getDate() - days);
    onDateFromChange(from.toISOString().split('T')[0]);
    onDateToChange(to.toISOString().split('T')[0]);
  }

  return (
    <div className="date-range-picker">
      <div className="date-range-presets">
        {PRESETS.map((preset) => (
          <button
            key={preset.days}
            type="button"
            className="secondary-button"
            onClick={() => handlePreset(preset.days)}
          >
            {preset.label}
          </button>
        ))}
      </div>
      <div className="date-range-inputs">
        <label>
          From
          <input
            type="date"
            value={dateFrom ?? ''}
            onChange={(e) => onDateFromChange(e.target.value || null)}
          />
        </label>
        <label>
          To
          <input
            type="date"
            value={dateTo ?? ''}
            onChange={(e) => onDateToChange(e.target.value || null)}
          />
        </label>
      </div>
    </div>
  );
}
