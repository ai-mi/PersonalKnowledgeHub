import { expect, it } from 'vitest'
import { itemsUrl } from './itemsApi'

const none = { type: '', tag: '', collection: '', unread: false, page: 1 }

it('sends only paging when no filters are set', () => {
  expect(itemsUrl(none)).toBe('/api/items?page=1&pageSize=20')
})

it('combines all filters, mapping tag to tags', () => {
  const url = new URL(itemsUrl({ type: 'note', tag: 'c#', collection: 'c1', unread: true, page: 3 }), 'http://x')

  expect(Object.fromEntries(url.searchParams)).toEqual({
    page: '3',
    pageSize: '20',
    type: 'note',
    tags: 'c#',
    collection: 'c1',
    unread: 'true',
  })
})
