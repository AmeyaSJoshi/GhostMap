namespace GhostMap.Shared.Domain
{
    /// <summary>
    /// The outcome of a validation check.
    ///
    /// GhostMap rejects bad scans rather than rendering them, so validation
    /// returns a reason the UI can show the user verbatim rather than a bare
    /// boolean.
    /// </summary>
    public readonly struct ValidationResult
    {
        public bool IsValid { get; }
        public string Error { get; }

        public ValidationResult(bool isValid, string error)
        {
            IsValid = isValid;
            Error = error;
        }

        public static ValidationResult Valid()
            => new ValidationResult(true, string.Empty);

        public static ValidationResult Invalid(string error)
            => new ValidationResult(false, error);

        public override string ToString()
            => IsValid ? "Valid" : $"Invalid: {Error}";
    }
}
