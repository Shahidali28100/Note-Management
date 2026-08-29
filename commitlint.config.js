module.exports = {
  extends: ['@commitlint/config-conventional'],
  plugins: [require('./.commitlint/ab-ticket-rule.js')],
  rules: {
    'ab-ticket-reference': [2, 'always'],
  },
}
