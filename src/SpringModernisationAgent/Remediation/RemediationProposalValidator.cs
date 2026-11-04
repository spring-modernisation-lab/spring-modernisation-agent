namespace SpringModernisationAgent.Remediation;

/// <summary>
/// Checks a model's proposal strictly for shape. It decides nothing about
/// content: the guardrails and Maven do.
/// </summary>
internal static class RemediationProposalValidator
{
    public static RemediationProposal Validate(
        RemediationProposal? proposal)
    {
        if (proposal is null)
        {
            throw new InvalidRemediationProposalException("The model returned no proposal.");
        }

        // Structured output can still yield null collections.
        proposal = proposal with
        {
            Edits = proposal.Edits ?? [],
            Options = proposal.Options ?? []
        };

        if (string.IsNullOrWhiteSpace(proposal.Diagnosis))
        {
            throw new InvalidRemediationProposalException("The proposal has no diagnosis.");
        }

        if (string.IsNullOrWhiteSpace(proposal.RootCause))
        {
            throw new InvalidRemediationProposalException("The proposal has no root cause.");
        }

        switch (proposal.Decision)
        {
            case RemediationDecision.Fix when proposal.Edits.Count == 0:
                throw new InvalidRemediationProposalException("A Fix proposal has no edits.");

            case RemediationDecision.CannotFix when proposal.Edits.Count > 0:
                throw new InvalidRemediationProposalException(
                    $"A {proposal.Decision} proposal must not contain edits.");

            case RemediationDecision.NeedsHumanDecision when proposal.Options.Count == 0:
                throw new InvalidRemediationProposalException(
                    "A NeedsHumanDecision proposal must list the options.");
        }

        for (var index = 0; index < proposal.Edits.Count; index++)
        {
            var edit = proposal.Edits[index];
            var number = index + 1;

            if (string.IsNullOrWhiteSpace(edit.Path))
            {
                throw new InvalidRemediationProposalException($"Proposal edit {number} has no path.");
            }

            if (string.IsNullOrWhiteSpace(edit.Rationale))
            {
                throw new InvalidRemediationProposalException($"Proposal edit {number} has no rationale.");
            }

            if (string.Equals(edit.Search ?? string.Empty, edit.Replace ?? string.Empty, StringComparison.Ordinal))
            {
                throw new InvalidRemediationProposalException($"Proposal edit {number} changes nothing.");
            }
        }

        return proposal;
    }
}
