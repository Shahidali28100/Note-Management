import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { HomePage } from '../pages/HomePage'

describe('HomePage', () => {
  it('HomePage_WhenRendered_DisplaysPlaceholderContent', () => {
    render(<HomePage />)

    expect(
      screen.getByRole('heading', { name: 'Note Management' }),
    ).toBeInTheDocument()
    expect(screen.getByText('Project scaffold — AB-1001')).toBeInTheDocument()
  })
})
