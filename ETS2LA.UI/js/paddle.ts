import { initializePaddle, getPaddleInstance } from "@paddle/paddle-js";
import type { Paddle, PricePreviewResponse } from "@paddle/paddle-js";

let paddle: Paddle | undefined;

/**
 * Initializes Paddle.js.
 *
 * @param token Paddle client side token
 * @param environment "sandbox" or "production".
 * @param eventCallback DotNetObjectReference used to forward checkout events.
 */
window.paddleInit = async function (
    token: string,
    environment: "sandbox" | "production",
    eventCallback?: any
): Promise<boolean> {
    try {
        paddle = await initializePaddle({
            token: token,
            environment: environment,
            eventCallback: eventCallback
                ? (event: any) => {
                      // Forwarded to .NET here via JS interop.
                      eventCallback.invokeMethodAsync("OnPaddleEvent", event);
                  }
                : undefined,
        });

        return paddle !== undefined;
    } catch (e) {
        console.error("[Paddle] Initialization failed", e);
        return false;
    }
};

window.paddlePricePreview = async function (
    priceIds: string[]
): Promise<PricePreviewResponse | null> {
    const instance = paddle ?? getPaddleInstance();
    if (!instance) {
        console.error("[Paddle] PricePreview called before initialization");
        return null;
    }

    try {
        return await instance.PricePreview({
            items: priceIds.map((priceId) => ({ priceId: priceId, quantity: 1 })),
        });
    } catch (e) {
        console.error("[Paddle] PricePreview failed", e);
        return null;
    }
};

window.paddleCheckoutOpen = function (
    priceIds: string[],
    userId: string,
    customerId?: string
): void {
    const instance = paddle ?? getPaddleInstance();
    if (!instance) {
        console.error("[Paddle] Checkout.Open called before initialization");
        return;
    }

    instance.Checkout.open({
        settings: {
            theme: "dark",
        },
        customData: {
            userId: userId
        },
        items: priceIds.map((priceId) => ({ priceId: priceId, quantity: 1 })),
        ...(customerId ? { customer: { id: customerId } } : {}),
    });
};

declare global {
    interface Window {
        paddleInit: (
            token: string,
            environment: "sandbox" | "production",
            eventCallback?: any
        ) => Promise<boolean>;
        paddlePricePreview: (priceIds: string[]) => Promise<PricePreviewResponse | null>;
        paddleCheckoutOpen: (priceIds: string[], customerId?: string) => void;
    }
}
