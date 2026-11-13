"use client";

import { useState, type FormEvent } from "react";

export type FormField = {
  name: string;
  label: string;
  type?: "text" | "email" | "date" | "time" | "number" | "select" | "textarea";
  placeholder?: string;
  required?: boolean;
  options?: string[];
  wide?: boolean;
};

type PreviewFormProps = {
  fields: FormField[];
  submitLabel: string;
};

export function PreviewForm({ fields, submitLabel }: PreviewFormProps) {
  const [submitted, setSubmitted] = useState(false);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitted(true);
  }

  return (
    <form className="preview-form" onSubmit={handleSubmit}>
      <div className="preview-form-grid">
        {fields.map((field) => (
          <label className={`preview-field${field.wide ? " preview-field-wide" : ""}`} htmlFor={field.name} key={field.name}>
            <span>{field.label}{field.required ? <span aria-hidden="true"> *</span> : null}</span>
            {field.type === "select" ? (
              <select defaultValue="" id={field.name} name={field.name} required={field.required}>
                <option disabled value="">Selecione</option>
                {field.options?.map((option) => <option key={option}>{option}</option>)}
              </select>
            ) : field.type === "textarea" ? (
              <textarea id={field.name} name={field.name} placeholder={field.placeholder} required={field.required} rows={3} />
            ) : (
              <input
                autoComplete={field.type === "email" ? "email" : undefined}
                id={field.name}
                name={field.name}
                placeholder={field.placeholder}
                required={field.required}
                type={field.type ?? "text"}
              />
            )}
          </label>
        ))}
      </div>
      <div className="preview-form-actions">
        <button className="button button-primary" type="submit">{submitLabel}</button>
        <span className="form-disclaimer">Prévia local: os dados não serão enviados nem salvos.</span>
      </div>
      {submitted ? (
        <p className="form-feedback" role="status">
          Demonstração concluída. Nenhum dado foi salvo; esta etapa será conectada à API.
        </p>
      ) : null}
    </form>
  );
}
