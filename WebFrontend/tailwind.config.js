module.exports = {
  future: {
    removeDeprecatedGapUtilities: true,
    purgeLayersByDefault: true,
  },
  purge: [
    './*.fs'
  ],
  theme: {
    extend: {},
  },
  variants: {
    border: ['responsive', 'group-hover', 'focus-within', 'first', 'last', 'odd', 'even', 'hover', 'focus', 'active', 'visited', 'disabled', 'motion-safe', 'motion-reduce'],
    appearance: ['responsive', 'group-hover', 'focus-within', 'first', 'last', 'odd', 'even', 'hover', 'focus', 'active', 'visited', 'disabled', 'motion-safe', 'motion-reduce'],
    backgroundColor: ['responsive', 'group-hover', 'focus-within', 'first', 'last', 'odd', 'even', 'hover', 'focus', 'active', 'visited', 'disabled', 'motion-safe', 'motion-reduce']
  },
  plugins: [],
}
