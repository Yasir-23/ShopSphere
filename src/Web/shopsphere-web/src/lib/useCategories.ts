"use client";

import { useEffect, useState } from "react";
import { getCategories, type Category } from "./catalog";

// Shared across components so the navbar and catalog make a single request.
let categoriesPromise: Promise<Category[]> | null = null;

export function loadCategories(): Promise<Category[]> {
  categoriesPromise ??= getCategories().catch(() => {
    categoriesPromise = null;
    return [];
  });
  return categoriesPromise;
}

export function useCategories(): Category[] {
  const [categories, setCategories] = useState<Category[]>([]);
  useEffect(() => {
    let active = true;
    loadCategories().then((c) => active && setCategories(c));
    return () => {
      active = false;
    };
  }, []);
  return categories;
}
