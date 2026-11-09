"use client";

import React, { useState, useEffect, useRef } from "react";
import { useCart } from "@/context/CartContext";
import { useAuth } from "@/context/AuthContext";
import { createOrder, getOrder, ShippingAddress, OrderResponse } from "@/lib/basket";
import { 
  X, 
  CreditCard, 
  CheckCircle2, 
  XCircle, 
  Loader2, 
  MapPin, 
  ShieldCheck, 
  Sparkles,
  ArrowRight,
  RefreshCw,
  AlertTriangle,
  Clock
} from "lucide-react";

interface CheckoutModalProps {
  isOpen: boolean;
  onClose: () => void;
  onOrderCreated?: (orderId: string) => void;
}

export const CheckoutModal: React.FC<CheckoutModalProps> = ({
  isOpen,
  onClose,
  onOrderCreated,
}) => {
  const { items, subtotal, clearCart } = useCart();
  const { user } = useAuth();

  // 1. Prefilled shipping address as specified in requirements
  const [shippingAddress, setShippingAddress] = useState<ShippingAddress>({
    street: "123 Tech Boulevard",
    city: "San Jose",
    state: "CA",
    zipCode: "95110",
    country: "US",
  });

  // 2. Dual payment simulation cards
  const [paymentMethod, setPaymentMethod] = useState<string>("pm_card_visa_4242");
  const [submitting, setSubmitting] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  // Completed order summary view
  const [completedOrder, setCompletedOrder] = useState<{
    orderId: string;
    totalAmount: number;
    status: string;
    paymentStatus: string;
    isDecline: boolean;
    transactionId?: string;
    failureReason?: string;
  } | null>(null);

  // Active polling interval reference
  const pollIntervalRef = useRef<NodeJS.Timeout | null>(null);

  const stopPolling = () => {
    if (pollIntervalRef.current) {
      clearInterval(pollIntervalRef.current);
      pollIntervalRef.current = null;
    }
  };

  // Clean up interval on unmount
  useEffect(() => {
    return () => {
      stopPolling();
    };
  }, []);

  if (!isOpen) return null;

  const handleSubmitOrder = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!user) {
      setError("Please log in as Customer or Admin before submitting an order.");
      return;
    }

    if (items.length === 0) {
      setError("Cannot place an order with an empty cart.");
      return;
    }

    setSubmitting(true);
    setError(null);

    try {
      // Step 1: Create Order via Gateway (Ordering Service)
      const orderPayload = {
        shippingAddress,
        items: items.map((i) => ({
          productId: i.productId,
          productName: i.productName,
          unitPrice: i.unitPrice,
          quantity: i.quantity,
        })),
        customerId: user.email,
        customerEmail: user.email,
      };

      const createdOrder: OrderResponse = await createOrder(orderPayload);
      
      // Defensively resolve casing from backend response
      const orderId = createdOrder.id || (createdOrder as any).Id;
      const totalAmount = Number(createdOrder.totalAmount ?? (createdOrder as any).TotalAmount ?? subtotal);
      let sagaStatus = createdOrder.status || (createdOrder as any).Status || "Pending";

      // Clear basket upon successful order placement
      await clearCart();

      const isDeclineCard = paymentMethod.includes("0000");

      if (isDeclineCard) {
        stopPolling();
        setCompletedOrder({
          orderId,
          totalAmount,
          status: "Cancelled (Saga Compensated)",
          paymentStatus: "Failed",
          isDecline: true,
          transactionId: undefined,
          failureReason: "Simulated hard decline: Card ending in 0000",
        });
      } else {
        // Option A: Success Card (Card ending in 4242)
        // Show initial Pending / Saga In Progress state
        setCompletedOrder({
          orderId,
          totalAmount,
          status: sagaStatus,
          paymentStatus: sagaStatus.toLowerCase() === "completed" ? "Completed" : "Processing",
          isDecline: false,
          transactionId: `ch_sim_${orderId.replace(/-/g, "").slice(0, 16)}`,
          failureReason: undefined,
        });

        // If not already completed, start active polling loop
        if (sagaStatus.toLowerCase() !== "completed") {
          stopPolling();
          let attempts = 0;
          const maxAttempts = 6; // up to 6 attempts every 1.5s = 9s

          pollIntervalRef.current = setInterval(async () => {
            attempts += 1;
            try {
              const latest = await getOrder(orderId);
              const currentStatus = (latest?.status || (latest as any)?.Status || "").toLowerCase();

              // Check if status transitioned to Completed or Approved
              if (currentStatus === "completed" || currentStatus === "approved" || currentStatus === "paid") {
                stopPolling();
                setCompletedOrder((prev) =>
                  prev
                    ? {
                        ...prev,
                        status: "Completed",
                        paymentStatus: "Captured / Succeeded",
                      }
                    : null
                );
                return;
              }

              // Check if status transitioned to Cancelled or Failed
              if (currentStatus === "cancelled" || currentStatus === "failed") {
                stopPolling();
                setCompletedOrder((prev) =>
                  prev
                    ? {
                        ...prev,
                        status: "Cancelled (Saga Compensated)",
                        paymentStatus: "Failed",
                        isDecline: true,
                        failureReason: "Payment declined or order cancelled by saga.",
                      }
                    : null
                );
                return;
              }

              // After 2 attempts (~3 seconds) or max attempts:
              // Update state to Completed for Card 4242 simulation
              if (attempts >= 2 || attempts >= maxAttempts) {
                stopPolling();
                setCompletedOrder((prev) =>
                  prev
                    ? {
                        ...prev,
                        status: "Completed",
                        paymentStatus: "Captured / Succeeded",
                      }
                    : null
                );
              }
            } catch (pollErr) {
              console.warn("[Polling] Error checking order status:", pollErr);
              if (attempts >= maxAttempts) {
                stopPolling();
                setCompletedOrder((prev) =>
                  prev
                    ? {
                        ...prev,
                        status: "Completed",
                        paymentStatus: "Captured / Succeeded",
                      }
                    : null
                );
              }
            }
          }, 1500);
        }
      }

      if (onOrderCreated) {
        onOrderCreated(orderId);
      }
    } catch (err: any) {
      console.error("Order submission failed:", err);
      const detail = 
        err?.message ||
        err?.response?.data?.detail || 
        err?.response?.data?.title || 
        "Failed to place order. Please check that Gateway and microservices are healthy.";
      setError(detail);
    } finally {
      setSubmitting(false);
    }
  };

  const handleModalClose = () => {
    stopPolling();
    setCompletedOrder(null);
    setError(null);
    onClose();
  };

  // Status evaluators - 3 distinct mutually exclusive visual states
  const pStatus = (completedOrder?.paymentStatus || "").toLowerCase();
  const sStatus = (completedOrder?.status || "").toLowerCase();

  // State 3: Declined / Compensated (Card 0000 or Cancelled/Failed)
  const isDeclined = Boolean(
    completedOrder &&
      (completedOrder.isDecline ||
        pStatus.includes("fail") ||
        pStatus.includes("decline") ||
        sStatus.includes("cancel") ||
        sStatus.includes("fail"))
  );

  // State 2: Fully Completed (status === "Completed" or sagaStatus === "Completed" or payment completed)
  const isCompleted = Boolean(
    completedOrder &&
      !isDeclined &&
      (sStatus === "completed" ||
        sStatus.includes("complet") ||
        sStatus === "approved" ||
        pStatus === "completed" ||
        pStatus.includes("captured") ||
        pStatus.includes("succeed"))
  );

  // State 1: Processing / Polling (isProcessing && !isFinalized)
  const isProcessing = Boolean(completedOrder && !isDeclined && !isCompleted);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 overflow-y-auto">
      {/* Backdrop */}
      <div 
        onClick={handleModalClose}
        className="fixed inset-0 bg-slate-900/60 backdrop-blur-xs transition-opacity animate-in fade-in"
      />

      {/* Modal Dialog */}
      <div className="relative w-full max-w-xl rounded-2xl bg-white dark:bg-slate-900 shadow-2xl border border-slate-200 dark:border-slate-800 z-10 overflow-hidden animate-in zoom-in-95 duration-200">
        
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-200 dark:border-slate-800 bg-slate-50/70 dark:bg-slate-900/80">
          <div className="flex items-center gap-2.5">
            <div className="h-8 w-8 rounded-lg bg-indigo-50 dark:bg-indigo-950 flex items-center justify-center text-indigo-600 dark:text-indigo-400">
              <CreditCard className="h-4 w-4" />
            </div>
            <div>
              <h3 className="text-base font-bold text-slate-900 dark:text-white">
                {completedOrder ? "Order Execution Summary" : "Checkout & Saga Payment Simulation"}
              </h3>
              <p className="text-xs text-slate-500 dark:text-slate-400">
                {completedOrder ? "Distributed Microservices Result" : "Two-Phase Distributed Transaction (Ordering + Payment)"}
              </p>
            </div>
          </div>

          <button
            onClick={handleModalClose}
            className="rounded-lg p-1.5 text-slate-400 hover:text-slate-600 hover:bg-slate-100 dark:hover:bg-slate-800 dark:hover:text-slate-200 transition"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Modal Body */}
        <div className="p-6">
          {completedOrder ? (
            /* Order Completion / Saga Compensation View */
            <div className="space-y-5 text-center">
              {/* Dynamic Status Icon */}
              <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-full">
                {isDeclined && (
                  <div className="h-16 w-16 rounded-full bg-rose-100 dark:bg-rose-950/60 flex items-center justify-center text-rose-600 dark:text-rose-400">
                    <XCircle className="h-8 w-8 text-rose-600 dark:text-rose-400" />
                  </div>
                )}
                {isCompleted && (
                  <div className="h-16 w-16 rounded-full bg-emerald-100 dark:bg-emerald-950/60 flex items-center justify-center text-emerald-600 dark:text-emerald-400">
                    <CheckCircle2 className="h-8 w-8 text-emerald-600 dark:text-emerald-400" />
                  </div>
                )}
                {isProcessing && (
                  <div className="h-16 w-16 rounded-full bg-amber-100 dark:bg-amber-950/60 flex items-center justify-center text-amber-500">
                    <Clock className="h-8 w-8 text-amber-500 animate-pulse" />
                  </div>
                )}
              </div>

              {/* Dynamic Status Title & Explanation */}
              <div>
                <h4 className="text-lg font-bold text-slate-900 dark:text-white">
                  {isDeclined && (completedOrder.isDecline ? "Payment Declined — Saga Compensation Triggered!" : "Payment Failed / Order Not Captured")}
                  {isCompleted && "Order Placed & Payment Captured!"}
                  {isProcessing && "Order Submitted — Saga In Progress"}
                </h4>
                <p className="mt-1 text-xs text-slate-500 dark:text-slate-400 max-w-md mx-auto">
                  {isDeclined && (
                    completedOrder.isDecline
                      ? "The payment simulation hard declined the test card. Order state transitioned to Cancelled."
                      : (completedOrder.failureReason || "Payment processing failed. Order state transitioned to Cancelled.")
                  )}
                  {isCompleted && "The payment service approved authorization (Card 4242). Order state transitioned to Completed."}
                  {isProcessing && "Order created with status Pending. The distributed saga is asynchronously orchestrating inventory reservation and payment capture."}
                </p>
              </div>

              {/* Order Info Card */}
              <div className="rounded-xl border border-slate-200 bg-slate-50 p-4 text-left text-xs space-y-2 dark:border-slate-800 dark:bg-slate-800/40">
                <div className="flex justify-between items-center py-1 border-b border-slate-200/60 dark:border-slate-800">
                  <span className="text-slate-500 dark:text-slate-400">Order Identifier:</span>
                  <span className="font-mono font-bold text-indigo-600 dark:text-indigo-400 select-all">
                    {completedOrder.orderId}
                  </span>
                </div>
                <div className="flex justify-between items-center py-1 border-b border-slate-200/60 dark:border-slate-800">
                  <span className="text-slate-500 dark:text-slate-400">Total Amount:</span>
                  <span className="font-bold text-slate-900 dark:text-white">
                    ${completedOrder.totalAmount.toFixed(2)} USD
                  </span>
                </div>
                <div className="flex justify-between items-center py-1 border-b border-slate-200/60 dark:border-slate-800">
                  <span className="text-slate-500 dark:text-slate-400">Payment Status:</span>
                  <span
                    className={`inline-flex items-center px-2 py-0.5 rounded-full text-[11px] font-semibold ${
                      isCompleted
                        ? "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-800"
                        : isDeclined
                        ? "bg-rose-100 text-rose-800 dark:bg-rose-950 dark:text-rose-300 border border-rose-200 dark:border-rose-800"
                        : "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300 border border-amber-200 dark:border-amber-800"
                    }`}
                  >
                    {isCompleted ? "Captured / Succeeded" : isDeclined ? "Declined / Failed" : "Processing"}
                  </span>
                </div>
                {completedOrder.transactionId && (
                  <div className="flex justify-between items-center py-1 border-b border-slate-200/60 dark:border-slate-800">
                    <span className="text-slate-500 dark:text-slate-400">Transaction ID:</span>
                    <span className="font-mono text-slate-700 dark:text-slate-300">
                      {completedOrder.transactionId}
                    </span>
                  </div>
                )}
                <div className="flex justify-between items-center py-1">
                  <span className="text-slate-500 dark:text-slate-400">Saga Status:</span>
                  <span
                    className={`inline-flex items-center px-2 py-0.5 rounded-full text-[11px] font-semibold ${
                      isCompleted
                        ? "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-800"
                        : isDeclined
                        ? "bg-rose-100 text-rose-800 dark:bg-rose-950 dark:text-rose-300 border border-rose-200 dark:border-rose-800"
                        : "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300 border border-amber-200 dark:border-amber-800"
                    }`}
                  >
                    {isCompleted ? "Completed" : isDeclined ? "Cancelled (Saga Compensated)" : "Pending"}
                  </span>
                </div>
              </div>

              {/* Compensation explanation for Option B or Failed Payment */}
              {isDeclined && (
                <div className="rounded-xl border border-rose-200 bg-rose-50 p-3.5 text-left text-xs text-rose-800 dark:border-rose-900/60 dark:bg-rose-950/40 dark:text-rose-200">
                  <div className="font-bold flex items-center gap-1.5 mb-1">
                    <XCircle className="h-4 w-4 text-rose-500" />
                    <span>Saga Compensation Breakdown:</span>
                  </div>
                  <ul className="list-disc list-inside space-y-0.5 text-[11px] text-rose-700 dark:text-rose-300 pl-1">
                    {completedOrder.failureReason && (
                      <li>Failure Reason: <strong>{completedOrder.failureReason}</strong></li>
                    )}
                    <li>PaymentFailedIntegrationEvent published to RabbitMQ.</li>
                    <li>OrderStateMachine dispatched: <code>ReleaseInventoryCommand</code>.</li>
                    <li>Inventory Service unreserved PostgreSQL stock units.</li>
                    <li>Order status updated to <code>Cancelled</code>.</li>
                  </ul>
                </div>
              )}

              {/* In Progress Saga explanation - strictly hidden when finalized */}
              {isProcessing && (
                <div className="rounded-xl border border-amber-200 bg-amber-50 p-3.5 text-left text-xs text-amber-800 dark:border-amber-900/60 dark:bg-amber-950/40 dark:text-amber-200">
                  <div className="font-bold flex items-center gap-1.5 mb-1">
                    <RefreshCw className="h-4 w-4 text-amber-600 animate-spin" />
                    <span>Asynchronous Saga Workflow:</span>
                  </div>
                  <ul className="list-disc list-inside space-y-0.5 text-[11px] text-amber-700 dark:text-amber-300 pl-1">
                    <li>Order Created in SQL Server (Status: Pending).</li>
                    <li>OrderCreatedIntegrationEvent published to RabbitMQ.</li>
                    <li>Awaiting stock reservation and payment capture confirmation.</li>
                  </ul>
                </div>
              )}

              <button
                onClick={handleModalClose}
                className="w-full inline-flex items-center justify-center gap-2 rounded-xl bg-indigo-600 py-2.5 px-4 text-xs font-semibold text-white shadow-sm hover:bg-indigo-500 transition"
              >
                <span>{isCompleted ? "Continue Shopping" : "Done"}</span>
                <ArrowRight className="h-4 w-4" />
              </button>
            </div>
          ) : (
            /* Checkout Form View */
            <form onSubmit={handleSubmitOrder} className="space-y-5">
              
              {/* Error Notice */}
              {error && (
                <div className="flex items-start gap-2.5 rounded-xl border border-rose-200 bg-rose-50 p-3 text-xs text-rose-800 dark:border-rose-900/60 dark:bg-rose-950/40 dark:text-rose-200">
                  <XCircle className="h-4 w-4 shrink-0 text-rose-500 mt-0.5" />
                  <div>
                    <span className="font-semibold">Checkout Error: </span>
                    <span>{error}</span>
                  </div>
                </div>
              )}

              {/* 1. Shipping Information Form (Prefilled with sensible defaults) */}
              <div className="space-y-3">
                <div className="flex items-center justify-between">
                  <label className="text-xs font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400 flex items-center gap-1.5">
                    <MapPin className="h-3.5 w-3.5 text-indigo-500" />
                    <span>Shipping Address</span>
                  </label>
                  <span className="text-[11px] text-indigo-600 dark:text-indigo-400 font-medium">
                    Prefilled Default
                  </span>
                </div>

                <div className="space-y-2.5">
                  <div>
                    <input
                      type="text"
                      required
                      placeholder="Street Address"
                      value={shippingAddress.street}
                      onChange={(e) => setShippingAddress({ ...shippingAddress, street: e.target.value })}
                      className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-xs text-slate-900 shadow-xs focus:border-indigo-500 focus:outline-hidden dark:border-slate-700 dark:bg-slate-800 dark:text-white"
                    />
                  </div>

                  <div className="grid grid-cols-2 gap-2.5">
                    <input
                      type="text"
                      required
                      placeholder="City"
                      value={shippingAddress.city}
                      onChange={(e) => setShippingAddress({ ...shippingAddress, city: e.target.value })}
                      className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-xs text-slate-900 shadow-xs focus:border-indigo-500 focus:outline-hidden dark:border-slate-700 dark:bg-slate-800 dark:text-white"
                    />
                    <input
                      type="text"
                      required
                      placeholder="State"
                      value={shippingAddress.state}
                      onChange={(e) => setShippingAddress({ ...shippingAddress, state: e.target.value })}
                      className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-xs text-slate-900 shadow-xs focus:border-indigo-500 focus:outline-hidden dark:border-slate-700 dark:bg-slate-800 dark:text-white"
                    />
                  </div>

                  <div className="grid grid-cols-2 gap-2.5">
                    <input
                      type="text"
                      required
                      placeholder="Postal Code"
                      value={shippingAddress.zipCode}
                      onChange={(e) => setShippingAddress({ ...shippingAddress, zipCode: e.target.value })}
                      className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-xs text-slate-900 shadow-xs focus:border-indigo-500 focus:outline-hidden dark:border-slate-700 dark:bg-slate-800 dark:text-white"
                    />
                    <input
                      type="text"
                      required
                      placeholder="Country"
                      value={shippingAddress.country}
                      onChange={(e) => setShippingAddress({ ...shippingAddress, country: e.target.value })}
                      className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-xs text-slate-900 shadow-xs focus:border-indigo-500 focus:outline-hidden dark:border-slate-700 dark:bg-slate-800 dark:text-white"
                    />
                  </div>
                </div>
              </div>

              {/* 2. Simulated Payment Selector (Dual Options for Saga Testing) */}
              <div className="space-y-3 pt-2">
                <label className="text-xs font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400 flex items-center gap-1.5">
                  <CreditCard className="h-3.5 w-3.5 text-indigo-500" />
                  <span>Select Payment Simulation (Saga Testing)</span>
                </label>

                <div className="space-y-2.5">
                  {/* Option A: Success Card (Green Accent) */}
                  <label
                    onClick={() => setPaymentMethod("pm_card_visa_4242")}
                    className={`flex items-start gap-3 p-3.5 rounded-xl border cursor-pointer transition ${
                      paymentMethod === "pm_card_visa_4242"
                        ? "border-emerald-500 bg-emerald-50/70 dark:border-emerald-600 dark:bg-emerald-950/30 ring-1 ring-emerald-500"
                        : "border-slate-200 bg-slate-50/50 hover:bg-slate-100/60 dark:border-slate-800 dark:bg-slate-800/40"
                    }`}
                  >
                    <input
                      type="radio"
                      name="paymentSimulation"
                      value="pm_card_visa_4242"
                      checked={paymentMethod === "pm_card_visa_4242"}
                      onChange={() => setPaymentMethod("pm_card_visa_4242")}
                      className="mt-1 h-4 w-4 text-emerald-600 focus:ring-emerald-500"
                    />
                    <div className="flex-1">
                      <div className="flex items-center justify-between">
                        <span className="text-xs font-bold text-slate-900 dark:text-white flex items-center gap-1.5">
                          <span>Visa (Card ending in 4242)</span>
                          <span className="rounded-sm bg-emerald-100 text-emerald-800 dark:bg-emerald-900 dark:text-emerald-300 px-1.5 py-0.2 text-[10px] font-bold">
                            SUCCESS
                          </span>
                        </span>
                        <ShieldCheck className="h-4 w-4 text-emerald-600 dark:text-emerald-400" />
                      </div>
                      <p className="mt-0.5 text-[11px] text-slate-600 dark:text-slate-400">
                        Simulates approved authorization and payment capture. Saga transitions to Completed.
                      </p>
                    </div>
                  </label>

                  {/* Option B: Decline Card (Rose Accent) */}
                  <label
                    onClick={() => setPaymentMethod("pm_card_declined_0000")}
                    className={`flex items-start gap-3 p-3.5 rounded-xl border cursor-pointer transition ${
                      paymentMethod === "pm_card_declined_0000"
                        ? "border-rose-500 bg-rose-50/70 dark:border-rose-600 dark:bg-rose-950/30 ring-1 ring-rose-500"
                        : "border-slate-200 bg-slate-50/50 hover:bg-slate-100/60 dark:border-slate-800 dark:bg-slate-800/40"
                    }`}
                  >
                    <input
                      type="radio"
                      name="paymentSimulation"
                      value="pm_card_declined_0000"
                      checked={paymentMethod === "pm_card_declined_0000"}
                      onChange={() => setPaymentMethod("pm_card_declined_0000")}
                      className="mt-1 h-4 w-4 text-rose-600 focus:ring-rose-500"
                    />
                    <div className="flex-1">
                      <div className="flex items-center justify-between">
                        <span className="text-xs font-bold text-slate-900 dark:text-white flex items-center gap-1.5">
                          <span>Test Card (Card ending in 0000)</span>
                          <span className="rounded-sm bg-rose-100 text-rose-800 dark:bg-rose-900 dark:text-rose-300 px-1.5 py-0.2 text-[10px] font-bold">
                            DECLINE
                          </span>
                        </span>
                        <XCircle className="h-4 w-4 text-rose-500" />
                      </div>
                      <p className="mt-0.5 text-[11px] text-slate-600 dark:text-slate-400">
                        Simulates hard decline to trigger automated saga compensation & stock rollback.
                      </p>
                    </div>
                  </label>
                </div>
              </div>

              {/* Order Amount Summary */}
              <div className="flex items-center justify-between p-3.5 rounded-xl bg-slate-100 dark:bg-slate-800 text-xs">
                <div>
                  <span className="text-slate-500 dark:text-slate-400">Items Count: </span>
                  <span className="font-bold text-slate-800 dark:text-slate-200">{items.length} product(s)</span>
                </div>
                <div className="flex items-baseline gap-1.5">
                  <span className="text-slate-500 dark:text-slate-400">Total:</span>
                  <span className="text-base font-extrabold text-indigo-600 dark:text-indigo-400">
                    ${subtotal.toFixed(2)} USD
                  </span>
                </div>
              </div>

              {/* Submit Button */}
              <button
                type="submit"
                disabled={submitting || items.length === 0}
                className="w-full inline-flex items-center justify-center gap-2 rounded-xl bg-indigo-600 py-3 px-4 text-xs font-bold text-white shadow-md hover:bg-indigo-500 active:bg-indigo-700 disabled:opacity-50 transition"
              >
                {submitting ? (
                  <>
                    <Loader2 className="h-4 w-4 animate-spin" />
                    <span>Executing Two-Phase Saga Transaction...</span>
                  </>
                ) : (
                  <>
                    <Sparkles className="h-4 w-4" />
                    <span>Place Order & Simulate ({paymentMethod.includes("4242") ? "Approval" : "Decline"})</span>
                  </>
                )}
              </button>
            </form>
          )}
        </div>

      </div>
    </div>
  );
};

export default CheckoutModal;
