import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 p-8">
      <h1 className="text-3xl font-semibold">404 — Page not found</h1>
      <Link to="/" className="text-blue-600 underline">
        Go back home
      </Link>
    </main>
  )
}
