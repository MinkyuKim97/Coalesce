const btn = document.getElementById("confirmBtn");
const statusEl = document.getElementById("status");

function setStatus(message, type) {
  statusEl.textContent = message;
  statusEl.classList.remove("status--error", "status--success");
  if (type === "error") statusEl.classList.add("status--error");
  if (type === "success") statusEl.classList.add("status--success");
}

function clearStatusAfter(ms) {
  setTimeout(() => {
    statusEl.textContent = "";
    statusEl.classList.remove("status--error", "status--success");
  }, ms);
}

// ripple visual feedback on click
function makeRipple(e){
  const rect = btn.getBoundingClientRect();
  const size = Math.max(rect.width, rect.height);
  const x = e.clientX - rect.left - size / 2;
  const y = e.clientY - rect.top - size / 2;

  const ripple = document.createElement("span");
  ripple.className = "ripple";
  ripple.style.width = ripple.style.height = size + "px";
  ripple.style.left = x + "px";
  ripple.style.top = y + "px";
  btn.appendChild(ripple);

  ripple.addEventListener("animationend", () => ripple.remove());
}

function getFormData() {
  return {
    firstName: document.getElementById("firstName").value.trim(),
    lastName: document.getElementById("lastName").value.trim(),
    email: document.getElementById("email").value.trim(),
  };
}

/**
 * Requirement: if any field is missing and user presses confirm,
 * show warning/error message.
 */
function validateForm(data) {
  const missing = [];
  if (!data.firstName) missing.push("First Name");
  if (!data.lastName) missing.push("Last Name");
  if (!data.email) missing.push("Email");

  if (missing.length > 0) {
    setStatus(`Please fill in: ${missing.join(", ")}`, "error");
    return false;
  }
  return true;
}

btn.addEventListener("click", (e) => {
  makeRipple(e);

  const data = getFormData();

  // validate first
  if (!validateForm(data)) return;

  // debug output
  console.log("Received Data:");
  console.log("First Name:", data.firstName);
  console.log("Last Name:", data.lastName);
  console.log("Email:", data.email);

  setStatus("All fields received ✔ (logged to console)", "success");
  clearStatusAfter(1500);
});
