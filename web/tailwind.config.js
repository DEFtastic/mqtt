/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        defcon: {
          black: '#0a0a0a',
          green: '#00ff00',
          'dark-green': '#003300',
        },
        meshtastic: {
          blue: '#0066cc',
          'light-blue': '#0099ff',
        }
      },
      fontFamily: {
        mono: ['JetBrains Mono', 'monospace'],
      },
    },
  },
  plugins: [],
} 