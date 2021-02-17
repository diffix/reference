module.exports = {
  purge: [
    "src/Website.Server/Pages/*",
    "src/Website.Client/wwwroot/*.html",
    "src/Website.Client/*.fs"
  ],
  darkMode: false, // or 'media' or 'class'
  theme: {
    extend: {},
  },
  variants: {
    extend: {},
  },
  plugins: [
    require('@tailwindcss/typography')
  ],
}
