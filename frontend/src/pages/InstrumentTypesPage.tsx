import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { instrumentTypesApi } from '../api/client'
import { useState } from 'react'

export function InstrumentTypesPage() {
  const queryClient = useQueryClient()
  const [futuresTypesEdit, setFuturesTypesEdit] = useState<string | null>(null)
  const [optionsTypesEdit, setOptionsTypesEdit] = useState<string | null>(null)
  const [toastMessage, setToastMessage] = useState<{ text: string; type: 'success' | 'error' } | null>(null)

  // Fetch futures types
  const { data: futuresTypesResp, isLoading: futuresLoading } = useQuery({
    queryKey: ['instrument-types-futures'],
    queryFn: () => instrumentTypesApi.getFuturesTypes(),
    staleTime: 30_000,
  })
  const futuresTypes = futuresTypesResp?.data || ''

  // Fetch options types
  const { data: optionsTypesResp, isLoading: optionsLoading } = useQuery({
    queryKey: ['instrument-types-options'],
    queryFn: () => instrumentTypesApi.getOptionsTypes(),
    staleTime: 30_000,
  })
  const optionsTypes = optionsTypesResp?.data || ''

  // Update futures types mutation
  const updateFuturesMutation = useMutation({
    mutationFn: (types: string) => instrumentTypesApi.updateFuturesTypes(types),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['instrument-types-futures'] })
      setFuturesTypesEdit(null)
      showToast('Futures types updated successfully', 'success')
    },
    onError: (err: any) => {
      showToast(`Failed to update: ${err.response?.data?.error || err.message}`, 'error')
    },
  })

  // Update options types mutation
  const updateOptionsMutation = useMutation({
    mutationFn: (types: string) => instrumentTypesApi.updateOptionsTypes(types),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['instrument-types-options'] })
      setOptionsTypesEdit(null)
      showToast('Options types updated successfully', 'success')
    },
    onError: (err: any) => {
      showToast(`Failed to update: ${err.response?.data?.error || err.message}`, 'error')
    },
  })

  // Reset defaults mutation
  const resetMutation = useMutation({
    mutationFn: () => instrumentTypesApi.resetDefaults(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['instrument-types-futures'] })
      queryClient.invalidateQueries({ queryKey: ['instrument-types-options'] })
      setFuturesTypesEdit(null)
      setOptionsTypesEdit(null)
      showToast('Reverted to default type mappings', 'success')
    },
    onError: (err: any) => {
      showToast(`Reset failed: ${err.response?.data?.error || err.message}`, 'error')
    },
  })

  const showToast = (text: string, type: 'success' | 'error') => {
    setToastMessage({ text, type })
    setTimeout(() => setToastMessage(null), 3000)
  }

  const handleSaveFutures = () => {
    if (!futuresTypesEdit?.trim()) {
      showToast('Futures types cannot be empty', 'error')
      return
    }
    updateFuturesMutation.mutate(futuresTypesEdit)
  }

  const handleSaveOptions = () => {
    if (!optionsTypesEdit?.trim()) {
      showToast('Options types cannot be empty', 'error')
      return
    }
    updateOptionsMutation.mutate(optionsTypesEdit)
  }

  const isLoading = futuresLoading || optionsLoading
  const isSaving = updateFuturesMutation.isPending || updateOptionsMutation.isPending || resetMutation.isPending

  return (
    <div className="p-6 max-w-4xl">
      <div className="mb-6 flex justify-between items-start">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Instrument Type Mappings</h1>
          <p className="text-gray-600 mt-1">
            Customize which broker-specific type codes classify as futures or options.
          </p>
        </div>
        <button
          onClick={() => {
            if (confirm('Reset to default type mappings?')) {
              resetMutation.mutate()
            }
          }}
          disabled={isSaving}
          className="px-4 py-2 bg-gray-200 text-gray-700 rounded hover:bg-gray-300 disabled:opacity-50"
        >
          🔄 Reset Defaults
        </button>
      </div>

      {/* Info banner */}
      <div className="mb-6 p-4 bg-blue-50 border border-blue-200 rounded text-sm text-blue-800">
        <strong>ℹ️ How this works:</strong> When instruments are downloaded, their type codes are matched against these lists
        to determine classification. Changes take effect on the next refresh.
      </div>

      {/* Toast notification */}
      {toastMessage && (
        <div
          className={`fixed bottom-4 right-4 px-4 py-3 rounded shadow ${
            toastMessage.type === 'success'
              ? 'bg-green-100 text-green-800 border border-green-300'
              : 'bg-red-100 text-red-800 border border-red-300'
          }`}
        >
          {toastMessage.text}
        </div>
      )}

      {isLoading ? (
        <div className="text-center py-12 text-gray-500">Loading...</div>
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {/* Futures Types Section */}
          <div className="border rounded-lg p-6 bg-white shadow-sm">
            <h2 className="text-lg font-semibold text-gray-900 mb-4">📈 Futures Types</h2>
            <div className="space-y-3">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-2">Type Codes (comma-separated)</label>
                <textarea
                  value={futuresTypesEdit ?? futuresTypes}
                  onChange={(e) => setFuturesTypesEdit(e.target.value)}
                  className="w-full h-24 p-3 border border-gray-300 rounded font-mono text-sm resize-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  placeholder="FUT,FUTIDX,FUTSTK,FUTURES,IF,SF"
                />
              </div>
              <div className="flex gap-2">
                <button
                  onClick={handleSaveFutures}
                  disabled={isSaving || futuresTypesEdit === null}
                  className="flex-1 px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
                >
                  {updateFuturesMutation.isPending ? '💾 Saving...' : '💾 Save'}
                </button>
                {futuresTypesEdit !== null && (
                  <button
                    onClick={() => setFuturesTypesEdit(null)}
                    className="px-4 py-2 bg-gray-200 text-gray-700 rounded hover:bg-gray-300"
                  >
                    ✕
                  </button>
                )}
              </div>
              {futuresTypesEdit === null && (
                <div className="text-xs text-gray-500 pt-2">
                  {futuresTypes.split(',').length} type(s) configured
                </div>
              )}
            </div>
          </div>

          {/* Options Types Section */}
          <div className="border rounded-lg p-6 bg-white shadow-sm">
            <h2 className="text-lg font-semibold text-gray-900 mb-4">📊 Options Types</h2>
            <div className="space-y-3">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-2">Type Codes (comma-separated)</label>
                <textarea
                  value={optionsTypesEdit ?? optionsTypes}
                  onChange={(e) => setOptionsTypesEdit(e.target.value)}
                  className="w-full h-24 p-3 border border-gray-300 rounded font-mono text-sm resize-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  placeholder="OPT,OPTIDX,OPTSTK,OPTIONS,CE,PE,IO,SO"
                />
              </div>
              <div className="flex gap-2">
                <button
                  onClick={handleSaveOptions}
                  disabled={isSaving || optionsTypesEdit === null}
                  className="flex-1 px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
                >
                  {updateOptionsMutation.isPending ? '💾 Saving...' : '💾 Save'}
                </button>
                {optionsTypesEdit !== null && (
                  <button
                    onClick={() => setOptionsTypesEdit(null)}
                    className="px-4 py-2 bg-gray-200 text-gray-700 rounded hover:bg-gray-300"
                  >
                    ✕
                  </button>
                )}
              </div>
              {optionsTypesEdit === null && (
                <div className="text-xs text-gray-500 pt-2">
                  {optionsTypes.split(',').length} type(s) configured
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Reference section */}
      <div className="mt-8 p-4 bg-gray-50 border border-gray-200 rounded text-sm text-gray-700">
        <h3 className="font-semibold mb-3">📚 Common Type Codes by Broker</h3>
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <div>
            <p className="font-medium mb-2">🟠 Futures:</p>
            <code className="block bg-white p-2 rounded border border-gray-300 text-xs mb-3">
              FUT, FUTIDX, FUTSTK, FUTURES (NSE/NFO), IF, SF (BFO), FUTCOM, FUTCUR, FUTIRD
            </code>
          </div>
          <div>
            <p className="font-medium mb-2">🟣 Options:</p>
            <code className="block bg-white p-2 rounded border border-gray-300 text-xs">
              OPT, OPTIDX, OPTSTK, OPTIONS (NSE/NFO), CE/PE (Zerodha), IO/SO (BFO)
            </code>
          </div>
        </div>
      </div>
    </div>
  )
}
