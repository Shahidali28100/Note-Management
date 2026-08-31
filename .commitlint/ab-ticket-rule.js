/**
 * @commitlint/config-conventional enforces `type(scope): description` (Conventional Commits)
 * but knows nothing about this repo's required `AB#<ticket>` suffix (CLAUDE.md "Commit Message
 * Format": `type(scope): description AB#ticket`, AGENTS.md commit examples). This local rule
 * closes that gap.
 *
 * Checks the SUBJECT line only, not the whole raw message — CLAUDE.md's format puts the ticket
 * reference at the end of the subject, not the end of the message. Checking the raw message tail
 * would wrongly reject any commit with a body/footer after the ticket ref (e.g. a standard
 * trailer like `Co-Authored-By:` following a `Relates to AB#1001` line).
 */

/** @type {import('@commitlint/types').Plugin} */
module.exports = {
  rules: {
    'ab-ticket-reference': (parsed) => {
      const subject = (parsed.subject ?? '').trim()
      const ok = /AB#\d+\s*$/.test(subject)
      return [
        ok,
        'commit subject line must end with "AB#<ticket-number>" (e.g. "feat(auth): add jwt authentication AB#1002")',
      ]
    },
  },
}
