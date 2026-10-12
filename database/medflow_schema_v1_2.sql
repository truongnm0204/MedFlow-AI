--
-- PostgreSQL database dump
--

\restrict wGTCh8gPLgg296ZEmglqLJv8XL2RG6tEasNGz7isAaQgwFxGficsyw7gaFARlkO

-- Dumped from database version 18.3
-- Dumped by pg_dump version 18.3

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: medflow; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA medflow;


--
-- Name: guard_amendment_finalized_parent(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.guard_amendment_finalized_parent() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'medflow', 'public'
    AS $$
DECLARE parent_status text;
BEGIN
 SELECT status INTO parent_status FROM medical_records WHERE id=NEW.medical_record_id;
 IF parent_status IS DISTINCT FROM 'FINALIZED' THEN
   RAISE EXCEPTION 'Medical record amendments apply only to finalized records';
 END IF;
 RETURN NEW;
END $$;


--
-- Name: guard_catalog_department(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.guard_catalog_department() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'medflow', 'public'
    AS $$
DECLARE dept_h uuid;
BEGIN
 IF NEW.department_id IS NOT NULL THEN
   SELECT hospital_id INTO dept_h FROM departments WHERE id=NEW.department_id;
   IF dept_h IS DISTINCT FROM NEW.hospital_id THEN
     RAISE EXCEPTION '% has department from another hospital', TG_TABLE_NAME;
   END IF;
 END IF;
 RETURN NEW;
END $$;


--
-- Name: guard_diagnostic_order_service(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.guard_diagnostic_order_service() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'medflow', 'public'
    AS $$
DECLARE v_h uuid; s_h uuid; s_type text; d_h uuid;
BEGIN
 SELECT hospital_id INTO v_h FROM visits WHERE id=NEW.visit_id;
 SELECT hospital_id,service_type INTO s_h,s_type FROM services WHERE id=NEW.service_id;
 SELECT staff.hospital_id INTO d_h FROM doctors JOIN staff ON staff.id=doctors.staff_id WHERE doctors.id=NEW.ordering_doctor_id;
 IF v_h IS DISTINCT FROM s_h OR v_h IS DISTINCT FROM d_h OR s_type <> 'DIAGNOSTIC' THEN
    RAISE EXCEPTION 'Diagnostic Order must use matching-hospital DIAGNOSTIC service and doctor';
 END IF;
 RETURN NEW;
END $$;


--
-- Name: guard_dispense_batch_medicine(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.guard_dispense_batch_medicine() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'medflow', 'public'
    AS $$
DECLARE expected_med uuid;actual_med uuid;
BEGIN
 SELECT pi.medicine_id INTO expected_med
 FROM dispense_items di JOIN prescription_items pi ON pi.id=di.prescription_item_id
 WHERE di.id=NEW.dispense_item_id;
 SELECT medicine_id INTO actual_med FROM medicine_batches WHERE id=NEW.medicine_batch_id;
 IF expected_med IS DISTINCT FROM actual_med THEN
    RAISE EXCEPTION 'Dispensed medicine batch must match prescribed medicine';
 END IF;
 RETURN NEW;
END $$;


--
-- Name: guard_dispense_item_prescription(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.guard_dispense_item_prescription() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'medflow', 'public'
    AS $$
DECLARE base_rx uuid; item_rx uuid;
BEGIN
 SELECT prescription_id INTO base_rx FROM dispenses WHERE id=NEW.dispense_id;
 SELECT prescription_id INTO item_rx FROM prescription_items WHERE id=NEW.prescription_item_id;
 IF base_rx IS DISTINCT FROM item_rx THEN
   RAISE EXCEPTION 'Dispense item must belong to dispense prescription';
 END IF;
 RETURN NEW;
END $$;


--
-- Name: guard_dispense_patient(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.guard_dispense_patient() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'medflow', 'public'
    AS $$
DECLARE rx_patient uuid;
BEGIN
 SELECT v.patient_id INTO rx_patient FROM prescriptions p JOIN visits v ON v.id=p.visit_id
 WHERE p.id=NEW.prescription_id;
 IF rx_patient IS DISTINCT FROM NEW.patient_id THEN
   RAISE EXCEPTION 'Dispense patient must match prescription Visit patient';
 END IF;
 RETURN NEW;
END $$;


--
-- Name: guard_full_dispense_completion(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.guard_full_dispense_completion() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'medflow', 'public'
    AS $$
DECLARE bad_count integer;
BEGIN
 IF NEW.status='COMPLETED' AND (TG_OP='INSERT' OR OLD.status IS DISTINCT FROM 'COMPLETED') THEN
   IF NEW.handed_over_by IS NULL OR NEW.handed_over_at IS NULL THEN
     RAISE EXCEPTION 'Completed dispense requires handover staff and time';
   END IF;
   SELECT count(*) INTO bad_count FROM prescription_items pi
   LEFT JOIN dispense_items di ON di.prescription_item_id=pi.id AND di.dispense_id=NEW.id
   WHERE pi.prescription_id=NEW.prescription_id
      AND (di.id IS NULL OR di.quantity_dispensed <> pi.quantity OR di.quantity_prepared <> pi.quantity
        OR (SELECT coalesce(sum(a.quantity),0) FROM dispense_batch_allocations a
              WHERE a.dispense_item_id=di.id AND a.allocation_status='CONSUMED') <> pi.quantity);
   IF bad_count > 0 THEN
     RAISE EXCEPTION 'Prescription is not fully dispensed; partial dispense disallowed';
   END IF;
 END IF;
 RETURN NEW;
END $$;


--
-- Name: guard_payment_allocation_visit(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.guard_payment_allocation_visit() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'medflow', 'public'
    AS $$
DECLARE p_visit uuid; c_visit uuid;
BEGIN
 SELECT visit_id INTO p_visit FROM payments WHERE id=NEW.payment_id;
 SELECT visit_id INTO c_visit FROM visit_charges WHERE id=NEW.charge_id;
 IF p_visit IS DISTINCT FROM c_visit THEN
   RAISE EXCEPTION 'Payment and charge must belong to the same Visit';
 END IF;
 RETURN NEW;
END $$;


--
-- Name: guard_prescription_item_hospital(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.guard_prescription_item_hospital() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'medflow', 'public'
    AS $$
DECLARE v_h uuid; m_h uuid;
BEGIN
 SELECT v.hospital_id INTO v_h FROM prescriptions p JOIN visits v ON v.id=p.visit_id
   WHERE p.id=NEW.prescription_id;
 SELECT hospital_id INTO m_h FROM medicines WHERE id=NEW.medicine_id;
 IF v_h IS DISTINCT FROM m_h THEN
   RAISE EXCEPTION 'Medicine and Prescription Visit must belong to same hospital';
 END IF;
 RETURN NEW;
END $$;


--
-- Name: guard_queue_entry_context(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.guard_queue_entry_context() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'medflow', 'public'
    AS $$
DECLARE queue_h uuid; queue_type_local text; v_h uuid; a_h uuid; v_p uuid; a_p uuid;
       order_visit uuid; order_service_h uuid;
BEGIN
 SELECT q.hospital_id,q.queue_type INTO queue_h,queue_type_local FROM queues q WHERE q.id=NEW.queue_id;
 IF NEW.visit_id IS NOT NULL THEN
   SELECT hospital_id, patient_id INTO v_h,v_p FROM visits WHERE id=NEW.visit_id;
   IF queue_h IS DISTINCT FROM v_h THEN RAISE EXCEPTION 'Queue/Visit hospital mismatch'; END IF;
 END IF;
 IF NEW.appointment_id IS NOT NULL THEN
   SELECT hospital_id,patient_id INTO a_h,a_p FROM appointments WHERE id=NEW.appointment_id;
   IF queue_h IS DISTINCT FROM a_h THEN RAISE EXCEPTION 'Queue/Appointment hospital mismatch'; END IF;
 END IF;
 IF NEW.visit_id IS NOT NULL AND NEW.appointment_id IS NOT NULL AND v_p IS DISTINCT FROM a_p THEN
   RAISE EXCEPTION 'Queue Visit/Appointment patient mismatch';
 END IF;
 IF NEW.diagnostic_order_id IS NOT NULL THEN
   SELECT visit_id INTO order_visit FROM diagnostic_orders WHERE id=NEW.diagnostic_order_id;
   IF NEW.visit_id IS NULL OR NEW.visit_id IS DISTINCT FROM order_visit OR queue_type_local <> 'DIAGNOSTIC' THEN
     RAISE EXCEPTION 'Diagnostic queue entry must match diagnostic order Visit/type';
   END IF;
 END IF;
 RETURN NEW;
END $$;


--
-- Name: guard_schedule_context(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.guard_schedule_context() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'medflow', 'public'
    AS $$
DECLARE s_h uuid; s_type text; r_h uuid; d_h uuid;
BEGIN
 SELECT hospital_id,service_type INTO s_h,s_type FROM services WHERE id=NEW.service_id;
 IF s_h IS DISTINCT FROM NEW.hospital_id OR s_type <> 'EXAMINATION' THEN
    RAISE EXCEPTION 'Schedule requires EXAMINATION service within same hospital';
 END IF;
 IF NEW.room_id IS NOT NULL THEN
   SELECT hospital_id INTO r_h FROM rooms WHERE id=NEW.room_id;
   IF r_h IS DISTINCT FROM NEW.hospital_id THEN RAISE EXCEPTION 'Schedule room hospital mismatch'; END IF;
 END IF;
 IF NEW.doctor_id IS NOT NULL THEN
   SELECT staff.hospital_id INTO d_h FROM doctors JOIN staff ON staff.id=doctors.staff_id
     WHERE doctors.id=NEW.doctor_id;
   IF d_h IS DISTINCT FROM NEW.hospital_id THEN RAISE EXCEPTION 'Schedule doctor hospital mismatch'; END IF;
 END IF;
 RETURN NEW;
END $$;


--
-- Name: guard_visit_charge_link(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.guard_visit_charge_link() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'medflow', 'public'
    AS $$
DECLARE rx_visit uuid; serv_h uuid; v_h uuid;
BEGIN
 SELECT hospital_id INTO v_h FROM visits WHERE id=NEW.visit_id;
 IF NEW.prescription_item_id IS NOT NULL THEN
   SELECT p.visit_id INTO rx_visit FROM prescription_items pi
     JOIN prescriptions p ON p.id=pi.prescription_id WHERE pi.id=NEW.prescription_item_id;
   IF NEW.source_type IS DISTINCT FROM 'MEDICATION' OR rx_visit IS DISTINCT FROM NEW.visit_id THEN
     RAISE EXCEPTION 'Medication charge must reference an item prescribed for the same Visit';
   END IF;
 END IF;
 IF NEW.service_id IS NOT NULL THEN
   SELECT hospital_id INTO serv_h FROM services WHERE id=NEW.service_id;
   IF serv_h IS DISTINCT FROM v_h THEN RAISE EXCEPTION 'Charge service and Visit hospital mismatch'; END IF;
 END IF;
 RETURN NEW;
END $$;


--
-- Name: guard_visit_context(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.guard_visit_context() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'medflow', 'public'
    AS $$
DECLARE r_h uuid;d_h uuid;a_patient uuid;
BEGIN
 IF NEW.assigned_room_id IS NOT NULL THEN
   SELECT hospital_id INTO r_h FROM rooms WHERE id=NEW.assigned_room_id;
   IF r_h IS DISTINCT FROM NEW.hospital_id THEN RAISE EXCEPTION 'Visit room hospital mismatch'; END IF;
 END IF;
 IF NEW.assigned_doctor_id IS NOT NULL THEN
   SELECT staff.hospital_id INTO d_h FROM doctors JOIN staff ON staff.id=doctors.staff_id
    WHERE doctors.id=NEW.assigned_doctor_id;
   IF d_h IS DISTINCT FROM NEW.hospital_id THEN RAISE EXCEPTION 'Visit doctor hospital mismatch'; END IF;
 END IF;
 IF NEW.appointment_id IS NOT NULL THEN
   SELECT patient_id INTO a_patient FROM appointments WHERE id=NEW.appointment_id;
   IF NEW.patient_id IS DISTINCT FROM a_patient THEN RAISE EXCEPTION 'Visit patient and Appointment patient differ'; END IF;
 END IF;
 RETURN NEW;
END $$;


--
-- Name: prevent_decided_amendment_change(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.prevent_decided_amendment_change() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'medflow', 'public'
    AS $$
BEGIN
 IF TG_OP='DELETE' OR (TG_OP='UPDATE' AND OLD.status <> 'PENDING') THEN
   RAISE EXCEPTION 'Amendment history cannot be deleted or edited once decided';
 END IF;
 IF TG_OP='UPDATE' AND (NEW.medical_record_id<>OLD.medical_record_id
  OR NEW.field_name<>OLD.field_name OR NEW.previous_value IS DISTINCT FROM OLD.previous_value
  OR NEW.corrected_value<>OLD.corrected_value OR NEW.reason<>OLD.reason
  OR NEW.requested_by<>OLD.requested_by) THEN
   RAISE EXCEPTION 'Amendment payload is immutable';
 END IF;
 RETURN CASE WHEN TG_OP='DELETE' THEN OLD ELSE NEW END;
END $$;


--
-- Name: prevent_finalized_medical_record_change(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.prevent_finalized_medical_record_change() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
 IF TG_OP = 'DELETE' AND OLD.status = 'FINALIZED' THEN
   RAISE EXCEPTION 'Finalized medical records cannot be deleted';
 END IF;
 IF TG_OP = 'UPDATE' AND OLD.status = 'FINALIZED' AND NEW IS DISTINCT FROM OLD THEN
   RAISE EXCEPTION 'Finalized medical records are immutable; record a correction in an audited separate workflow';
 END IF;
 RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END $$;


--
-- Name: prevent_queue_renumber(); Type: FUNCTION; Schema: medflow; Owner: -
--

CREATE FUNCTION medflow.prevent_queue_renumber() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
 IF NEW.queue_number <> OLD.queue_number OR NEW.queue_id <> OLD.queue_id THEN
   RAISE EXCEPTION 'Queue number and queue identity are immutable';
 END IF;
 RETURN NEW;
END $$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: appointment_events; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.appointment_events (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    appointment_id uuid NOT NULL,
    event_type character varying(40) NOT NULL,
    old_status character varying(20),
    new_status character varying(20),
    old_schedule_id uuid,
    new_schedule_id uuid,
    reason text,
    actor_user_id uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: appointment_policies; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.appointment_policies (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hospital_id uuid NOT NULL,
    version integer NOT NULL,
    config jsonb DEFAULT '{}'::jsonb NOT NULL,
    effective_from timestamp with time zone NOT NULL,
    effective_to timestamp with time zone,
    status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    CONSTRAINT appointment_policies_check CHECK (((effective_to IS NULL) OR (effective_to > effective_from))),
    CONSTRAINT appointment_policies_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'INACTIVE'::character varying])::text[]))),
    CONSTRAINT appointment_policies_version_check CHECK ((version > 0))
);


--
-- Name: appointments; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.appointments (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    appointment_code character varying(40) NOT NULL,
    hospital_id uuid NOT NULL,
    patient_id uuid NOT NULL,
    schedule_id uuid NOT NULL,
    booking_type character varying(20) NOT NULL,
    scheduled_start timestamp with time zone NOT NULL,
    scheduled_end timestamp with time zone NOT NULL,
    status character varying(20) DEFAULT 'BOOKED'::character varying NOT NULL,
    booked_at timestamp with time zone DEFAULT now() NOT NULL,
    cancelled_at timestamp with time zone,
    created_by uuid,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    reason_for_booking text,
    symptoms text,
    booking_channel character varying(25) DEFAULT 'PATIENT_WEB'::character varying,
    CONSTRAINT appointments_booking_type_check CHECK (((booking_type)::text = ANY ((ARRAY['DOCTOR'::character varying, 'SERVICE'::character varying])::text[]))),
    CONSTRAINT appointments_check CHECK ((scheduled_end > scheduled_start)),
    CONSTRAINT appointments_status_check CHECK (((status)::text = ANY ((ARRAY['BOOKED'::character varying, 'CHECKED_IN'::character varying, 'CANCELLED'::character varying, 'NO_SHOW'::character varying])::text[]))),
    CONSTRAINT ck_appointments_channel CHECK (((booking_channel IS NULL) OR ((booking_channel)::text = ANY ((ARRAY['PATIENT_WEB'::character varying, 'STAFF_DESK'::character varying, 'HOTLINE'::character varying])::text[]))))
);


--
-- Name: audit_logs; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.audit_logs (
    id bigint NOT NULL,
    actor_user_id uuid,
    action character varying(100) NOT NULL,
    entity_type character varying(100) NOT NULL,
    entity_id uuid,
    old_value jsonb,
    new_value jsonb,
    reason text,
    ip_address inet,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: audit_logs_id_seq; Type: SEQUENCE; Schema: medflow; Owner: -
--

ALTER TABLE medflow.audit_logs ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME medflow.audit_logs_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: auth_verification_challenges; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.auth_verification_challenges (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid,
    purpose character varying(40) NOT NULL,
    channel character varying(15) NOT NULL,
    recipient_fingerprint bytea NOT NULL,
    secret_hash text NOT NULL,
    status character varying(20) DEFAULT 'PENDING'::character varying NOT NULL,
    attempts integer DEFAULT 0 NOT NULL,
    max_attempts integer DEFAULT 5 NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    verified_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT auth_verification_challenges_attempts_check CHECK ((attempts >= 0)),
    CONSTRAINT auth_verification_challenges_channel_check CHECK (((channel)::text = ANY ((ARRAY['EMAIL'::character varying, 'SMS'::character varying])::text[]))),
    CONSTRAINT auth_verification_challenges_check CHECK ((attempts <= max_attempts)),
    CONSTRAINT auth_verification_challenges_check1 CHECK ((((status)::text <> 'VERIFIED'::text) OR (verified_at IS NOT NULL))),
    CONSTRAINT auth_verification_challenges_check2 CHECK ((expires_at > created_at)),
    CONSTRAINT auth_verification_challenges_max_attempts_check CHECK (((max_attempts >= 1) AND (max_attempts <= 20))),
    CONSTRAINT auth_verification_challenges_purpose_check CHECK (((purpose)::text = ANY ((ARRAY['REGISTER_EMAIL'::character varying, 'CHANGE_EMAIL'::character varying, 'CHANGE_PHONE'::character varying, 'PASSWORD_RESET'::character varying])::text[]))),
    CONSTRAINT auth_verification_challenges_recipient_fingerprint_check CHECK ((octet_length(recipient_fingerprint) = 32)),
    CONSTRAINT auth_verification_challenges_status_check CHECK (((status)::text = ANY ((ARRAY['PENDING'::character varying, 'VERIFIED'::character varying, 'EXPIRED'::character varying, 'LOCKED'::character varying, 'REVOKED'::character varying])::text[])))
);


--
-- Name: COLUMN auth_verification_challenges.recipient_fingerprint; Type: COMMENT; Schema: medflow; Owner: -
--

COMMENT ON COLUMN medflow.auth_verification_challenges.recipient_fingerprint IS 'HMAC-SHA256 with an application-held secret, not plain SHA of email/phone.';


--
-- Name: COLUMN auth_verification_challenges.secret_hash; Type: COMMENT; Schema: medflow; Owner: -
--

COMMENT ON COLUMN medflow.auth_verification_challenges.secret_hash IS 'Slow password-style hash or keyed digest of a server-protected OTP; never store plaintext.';


--
-- Name: departments; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.departments (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hospital_id uuid NOT NULL,
    department_code character varying(40) NOT NULL,
    name character varying(255) NOT NULL,
    description text,
    status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    icon_name character varying(80),
    is_featured boolean DEFAULT false NOT NULL,
    display_order integer DEFAULT 0 NOT NULL,
    CONSTRAINT departments_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'INACTIVE'::character varying])::text[])))
);


--
-- Name: diagnostic_orders; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.diagnostic_orders (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    order_code character varying(40) NOT NULL,
    visit_id uuid NOT NULL,
    ordering_doctor_id uuid NOT NULL,
    service_id uuid NOT NULL,
    clinical_instruction text,
    priority character varying(20) DEFAULT 'ROUTINE'::character varying NOT NULL,
    status character varying(20) DEFAULT 'ORDERED'::character varying NOT NULL,
    ordered_at timestamp with time zone DEFAULT now() NOT NULL,
    confirmed_at timestamp with time zone,
    confirmed_by uuid,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT diagnostic_orders_priority_check CHECK (((priority)::text = ANY ((ARRAY['ROUTINE'::character varying, 'URGENT'::character varying])::text[]))),
    CONSTRAINT diagnostic_orders_status_check CHECK (((status)::text = ANY ((ARRAY['ORDERED'::character varying, 'CONFIRMED'::character varying, 'WAITING'::character varying, 'IN_PROGRESS'::character varying, 'COMPLETED'::character varying, 'CANCELLED'::character varying])::text[])))
);


--
-- Name: diagnostic_result_files; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.diagnostic_result_files (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    diagnostic_result_id uuid NOT NULL,
    file_key text NOT NULL,
    file_name character varying(255),
    content_type character varying(100),
    uploaded_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: diagnostic_results; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.diagnostic_results (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    diagnostic_order_id uuid NOT NULL,
    result_text text,
    conclusion text,
    status character varying(20) DEFAULT 'PENDING'::character varying NOT NULL,
    performed_by uuid,
    result_at timestamp with time zone,
    finalized_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT diagnostic_results_status_check CHECK (((status)::text = ANY ((ARRAY['PENDING'::character varying, 'DRAFT'::character varying, 'READY'::character varying, 'FINALIZED'::character varying])::text[])))
);


--
-- Name: dispense_batch_allocations; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.dispense_batch_allocations (
    dispense_item_id uuid NOT NULL,
    medicine_batch_id uuid NOT NULL,
    quantity integer NOT NULL,
    allocation_status character varying(15) DEFAULT 'RESERVED'::character varying NOT NULL,
    reserved_at timestamp with time zone DEFAULT now() NOT NULL,
    consumed_at timestamp with time zone,
    released_at timestamp with time zone,
    CONSTRAINT ck_batch_allocation_status CHECK (((allocation_status)::text = ANY ((ARRAY['RESERVED'::character varying, 'CONSUMED'::character varying, 'RELEASED'::character varying])::text[]))),
    CONSTRAINT dispense_batch_allocations_quantity_check CHECK ((quantity > 0))
);


--
-- Name: dispense_items; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.dispense_items (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    dispense_id uuid NOT NULL,
    prescription_item_id uuid NOT NULL,
    quantity_dispensed integer DEFAULT 0 NOT NULL,
    quantity_prepared integer DEFAULT 0 NOT NULL,
    CONSTRAINT ck_dispense_items_dispensed_nonneg CHECK ((quantity_dispensed >= 0)),
    CONSTRAINT ck_dispense_items_quantities CHECK ((quantity_dispensed <= quantity_prepared)),
    CONSTRAINT ck_dispense_prepared_nonneg CHECK ((quantity_prepared >= 0))
);


--
-- Name: dispenses; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.dispenses (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    prescription_id uuid NOT NULL,
    patient_id uuid NOT NULL,
    pharmacy_staff_id uuid,
    status character varying(20) DEFAULT 'REQUESTED'::character varying NOT NULL,
    dispensed_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    prepared_by uuid,
    prepared_at timestamp with time zone,
    handed_over_by uuid,
    handed_over_at timestamp with time zone,
    recipient_name character varying(255),
    recipient_relationship character varying(60),
    exception_reason text,
    CONSTRAINT ck_dispenses_status_v12 CHECK (((status)::text = ANY ((ARRAY['REQUESTED'::character varying, 'PREPARING'::character varying, 'READY_FOR_HANDOVER'::character varying, 'EXCEPTION'::character varying, 'COMPLETED'::character varying, 'CANCELLED'::character varying])::text[])))
);


--
-- Name: doctors; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.doctors (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    staff_id uuid NOT NULL,
    doctor_code character varying(40) NOT NULL,
    specialty character varying(200),
    professional_title character varying(120),
    status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    years_of_experience integer DEFAULT 0 NOT NULL,
    biography text,
    work_history text,
    CONSTRAINT doctors_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'INACTIVE'::character varying])::text[]))),
    CONSTRAINT doctors_years_of_experience_check CHECK ((years_of_experience >= 0))
);


--
-- Name: examination_schedules; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.examination_schedules (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hospital_id uuid NOT NULL,
    service_id uuid NOT NULL,
    doctor_id uuid,
    room_id uuid,
    starts_at timestamp with time zone NOT NULL,
    ends_at timestamp with time zone NOT NULL,
    capacity integer NOT NULL,
    booked_count integer DEFAULT 0 NOT NULL,
    status character varying(20) DEFAULT 'OPEN'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT examination_schedules_capacity_check CHECK ((capacity > 0)),
    CONSTRAINT examination_schedules_check CHECK ((ends_at > starts_at)),
    CONSTRAINT examination_schedules_check1 CHECK (((booked_count >= 0) AND (booked_count <= capacity))),
    CONSTRAINT examination_schedules_status_check CHECK (((status)::text = ANY ((ARRAY['OPEN'::character varying, 'FULL'::character varying, 'CLOSED'::character varying, 'EXPIRED'::character varying])::text[])))
);


--
-- Name: hospitals; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.hospitals (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hospital_code character varying(40) NOT NULL,
    name character varying(255) NOT NULL,
    address text,
    phone character varying(30),
    email character varying(255),
    status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT hospitals_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'INACTIVE'::character varying])::text[])))
);


--
-- Name: identity_verifications; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.identity_verifications (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    patient_id uuid NOT NULL,
    requested_by uuid,
    provider character varying(40) NOT NULL,
    provider_reference character varying(180),
    document_fingerprint bytea,
    document_last4 character varying(4),
    status character varying(20) DEFAULT 'PENDING'::character varying NOT NULL,
    requested_at timestamp with time zone DEFAULT now() NOT NULL,
    verified_at timestamp with time zone,
    verified_by uuid,
    expires_at timestamp with time zone,
    failure_reason_code character varying(60),
    CONSTRAINT identity_verifications_check CHECK ((((status)::text <> 'VERIFIED'::text) OR (verified_at IS NOT NULL))),
    CONSTRAINT identity_verifications_document_fingerprint_check CHECK (((document_fingerprint IS NULL) OR (octet_length(document_fingerprint) = 32))),
    CONSTRAINT identity_verifications_document_last4_check CHECK (((document_last4 IS NULL) OR ((document_last4)::text ~ '^[0-9]{4}$'::text))),
    CONSTRAINT identity_verifications_provider_check CHECK (((provider)::text = ANY ((ARRAY['VNPT_EKYC'::character varying, 'STAFF_MANUAL'::character varying, 'OTHER_APPROVED'::character varying])::text[]))),
    CONSTRAINT identity_verifications_status_check CHECK (((status)::text = ANY ((ARRAY['PENDING'::character varying, 'VERIFIED'::character varying, 'FAILED'::character varying, 'EXPIRED'::character varying, 'REVOKED'::character varying])::text[])))
);


--
-- Name: TABLE identity_verifications; Type: COMMENT; Schema: medflow; Owner: -
--

COMMENT ON TABLE medflow.identity_verifications IS 'Stores minimal eKYC/identity verification metadata. Do not keep raw document images or IDs without explicit retention controls.';


--
-- Name: integration_configs; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.integration_configs (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hospital_id uuid,
    integration_code character varying(80) NOT NULL,
    provider character varying(100) NOT NULL,
    config jsonb DEFAULT '{}'::jsonb NOT NULL,
    secret_ref character varying(255),
    status character varying(20) DEFAULT 'INACTIVE'::character varying NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT integration_configs_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'INACTIVE'::character varying])::text[])))
);


--
-- Name: medical_record_amendments; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.medical_record_amendments (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    medical_record_id uuid NOT NULL,
    field_name character varying(45) NOT NULL,
    previous_value text,
    corrected_value text NOT NULL,
    reason text NOT NULL,
    status character varying(15) DEFAULT 'PENDING'::character varying NOT NULL,
    requested_by uuid NOT NULL,
    approved_by uuid,
    requested_at timestamp with time zone DEFAULT now() NOT NULL,
    decided_at timestamp with time zone,
    CONSTRAINT medical_record_amendments_check CHECK ((((status)::text = 'PENDING'::text) OR ((approved_by IS NOT NULL) AND (decided_at IS NOT NULL)))),
    CONSTRAINT medical_record_amendments_field_name_check CHECK (((field_name)::text = ANY ((ARRAY['chief_complaint'::character varying, 'symptoms'::character varying, 'clinical_findings'::character varying, 'clinical_notes'::character varying, 'clinical_conclusion'::character varying])::text[]))),
    CONSTRAINT medical_record_amendments_status_check CHECK (((status)::text = ANY ((ARRAY['PENDING'::character varying, 'APPROVED'::character varying, 'REJECTED'::character varying])::text[])))
);


--
-- Name: TABLE medical_record_amendments; Type: COMMENT; Schema: medflow; Owner: -
--

COMMENT ON TABLE medflow.medical_record_amendments IS 'Append-only correction requests; approved amendments are rendered on top of immutable finalized record, never silently UPDATE original.';


--
-- Name: medical_records; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.medical_records (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    visit_id uuid NOT NULL,
    chief_complaint text,
    symptoms text,
    clinical_findings text,
    clinical_notes text,
    clinical_conclusion text,
    status character varying(20) DEFAULT 'DRAFT'::character varying NOT NULL,
    finalized_at timestamp with time zone,
    finalized_by uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT medical_records_status_check CHECK (((status)::text = ANY ((ARRAY['DRAFT'::character varying, 'FINALIZED'::character varying])::text[])))
);


--
-- Name: medicine_batches; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.medicine_batches (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    medicine_id uuid NOT NULL,
    batch_number character varying(100) NOT NULL,
    manufacture_date date,
    expiry_date date NOT NULL,
    available_quantity integer DEFAULT 0 NOT NULL,
    status character varying(20) DEFAULT 'AVAILABLE'::character varying NOT NULL,
    reserved_quantity integer DEFAULT 0 NOT NULL,
    shelf_location character varying(100),
    CONSTRAINT ck_batch_reservation CHECK (((reserved_quantity >= 0) AND (reserved_quantity <= available_quantity))),
    CONSTRAINT medicine_batches_available_quantity_check CHECK ((available_quantity >= 0)),
    CONSTRAINT medicine_batches_check CHECK (((manufacture_date IS NULL) OR (expiry_date > manufacture_date))),
    CONSTRAINT medicine_batches_status_check CHECK (((status)::text = ANY ((ARRAY['AVAILABLE'::character varying, 'BLOCKED'::character varying, 'EXPIRED'::character varying, 'DEPLETED'::character varying])::text[])))
);


--
-- Name: COLUMN medicine_batches.available_quantity; Type: COMMENT; Schema: medflow; Owner: -
--

COMMENT ON COLUMN medflow.medicine_batches.available_quantity IS 'Physical on-hand; free-to-allocate = available_quantity - reserved_quantity.';


--
-- Name: medicine_prices; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.medicine_prices (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    medicine_id uuid NOT NULL,
    unit_price numeric(14,0) NOT NULL,
    effective_from timestamp with time zone NOT NULL,
    effective_to timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    CONSTRAINT medicine_prices_check CHECK (((effective_to IS NULL) OR (effective_to > effective_from))),
    CONSTRAINT medicine_prices_unit_price_check CHECK ((unit_price >= (0)::numeric))
);


--
-- Name: medicines; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.medicines (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hospital_id uuid NOT NULL,
    medicine_code character varying(40) NOT NULL,
    name character varying(255) NOT NULL,
    active_ingredient character varying(255),
    strength character varying(80),
    dosage_form character varying(80),
    unit character varying(30) NOT NULL,
    status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    CONSTRAINT medicines_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'INACTIVE'::character varying])::text[])))
);


--
-- Name: notifications; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.notifications (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    patient_id uuid,
    user_id uuid,
    event_type character varying(60) NOT NULL,
    channel character varying(20) NOT NULL,
    subject character varying(255),
    body text NOT NULL,
    status character varying(20) DEFAULT 'PENDING'::character varying NOT NULL,
    sent_at timestamp with time zone,
    error_message text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT notifications_channel_check CHECK (((channel)::text = ANY ((ARRAY['IN_APP'::character varying, 'EMAIL'::character varying, 'SMS'::character varying])::text[]))),
    CONSTRAINT notifications_check CHECK (((patient_id IS NOT NULL) OR (user_id IS NOT NULL))),
    CONSTRAINT notifications_status_check CHECK (((status)::text = ANY ((ARRAY['PENDING'::character varying, 'SENT'::character varying, 'FAILED'::character varying, 'READ'::character varying])::text[])))
);


--
-- Name: outbox_events; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.outbox_events (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    event_type character varying(100) NOT NULL,
    aggregate_type character varying(100) NOT NULL,
    aggregate_id uuid NOT NULL,
    payload jsonb NOT NULL,
    status character varying(20) DEFAULT 'PENDING'::character varying NOT NULL,
    attempts integer DEFAULT 0 NOT NULL,
    next_attempt_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    processed_at timestamp with time zone,
    CONSTRAINT outbox_events_attempts_check CHECK ((attempts >= 0)),
    CONSTRAINT outbox_events_status_check CHECK (((status)::text = ANY ((ARRAY['PENDING'::character varying, 'PROCESSING'::character varying, 'SENT'::character varying, 'FAILED'::character varying])::text[])))
);


--
-- Name: patient_allergies; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.patient_allergies (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    patient_id uuid NOT NULL,
    substance_name character varying(255) NOT NULL,
    allergen_type character varying(20) DEFAULT 'MEDICATION'::character varying NOT NULL,
    reaction text,
    severity character varying(25),
    verification_status character varying(25) DEFAULT 'PATIENT_REPORTED'::character varying NOT NULL,
    status character varying(15) DEFAULT 'ACTIVE'::character varying NOT NULL,
    reported_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_by uuid,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT patient_allergies_allergen_type_check CHECK (((allergen_type)::text = ANY ((ARRAY['MEDICATION'::character varying, 'FOOD'::character varying, 'OTHER'::character varying])::text[]))),
    CONSTRAINT patient_allergies_severity_check CHECK (((severity)::text = ANY ((ARRAY['MILD'::character varying, 'MODERATE'::character varying, 'SEVERE'::character varying, 'UNKNOWN'::character varying])::text[]))),
    CONSTRAINT patient_allergies_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'INACTIVE'::character varying])::text[]))),
    CONSTRAINT patient_allergies_verification_status_check CHECK (((verification_status)::text = ANY ((ARRAY['PATIENT_REPORTED'::character varying, 'CLINICALLY_CONFIRMED'::character varying, 'DISPUTED'::character varying])::text[])))
);


--
-- Name: patient_medical_history_entries; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.patient_medical_history_entries (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    patient_id uuid NOT NULL,
    history_type character varying(25) NOT NULL,
    title character varying(255) NOT NULL,
    details text,
    started_on date,
    ended_on date,
    source_visit_id uuid,
    recorded_by uuid,
    recorded_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT patient_medical_history_entries_check CHECK (((ended_on IS NULL) OR (started_on IS NULL) OR (ended_on >= started_on))),
    CONSTRAINT patient_medical_history_entries_history_type_check CHECK (((history_type)::text = ANY ((ARRAY['CHRONIC_CONDITION'::character varying, 'SURGERY'::character varying, 'FAMILY_HISTORY'::character varying, 'SOCIAL_HISTORY'::character varying, 'OTHER'::character varying])::text[])))
);


--
-- Name: patient_relationships; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.patient_relationships (
    owner_patient_id uuid NOT NULL,
    member_patient_id uuid NOT NULL,
    relationship_type character varying(40) NOT NULL,
    can_manage boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    can_book_appointments boolean DEFAULT false NOT NULL,
    can_view_medical_records boolean DEFAULT false NOT NULL,
    relationship_status character varying(20) DEFAULT 'PENDING'::character varying NOT NULL,
    consent_verified_at timestamp with time zone,
    consent_verified_by uuid,
    revoked_at timestamp with time zone,
    CONSTRAINT ck_patient_relationship_status CHECK (((relationship_status)::text = ANY ((ARRAY['PENDING'::character varying, 'ACTIVE'::character varying, 'REVOKED'::character varying, 'NEEDS_REVIEW'::character varying])::text[]))),
    CONSTRAINT patient_relationships_check CHECK ((owner_patient_id <> member_patient_id))
);


--
-- Name: patients; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.patients (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    patient_code character varying(40) NOT NULL,
    user_id uuid,
    full_name character varying(255) NOT NULL,
    date_of_birth date,
    gender character varying(20),
    phone character varying(30),
    email character varying(255),
    identification_no character varying(50),
    address text,
    status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    avatar_url text,
    nationality character varying(100),
    blood_type character varying(5),
    emergency_contact_name character varying(255),
    emergency_contact_phone character varying(30),
    emergency_contact_relationship character varying(60),
    identity_issued_date date,
    identity_issued_place character varying(255),
    CONSTRAINT ck_patients_blood_type CHECK (((blood_type IS NULL) OR ((blood_type)::text = ANY ((ARRAY['A+'::character varying, 'A-'::character varying, 'B+'::character varying, 'B-'::character varying, 'AB+'::character varying, 'AB-'::character varying, 'O+'::character varying, 'O-'::character varying])::text[])))),
    CONSTRAINT patients_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'INACTIVE'::character varying])::text[])))
);


--
-- Name: payment_allocations; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.payment_allocations (
    payment_id uuid NOT NULL,
    charge_id uuid NOT NULL,
    amount numeric(14,0) NOT NULL,
    CONSTRAINT payment_allocations_amount_check CHECK ((amount > (0)::numeric))
);


--
-- Name: payment_gateway_events; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.payment_gateway_events (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    provider character varying(40) NOT NULL,
    provider_event_id character varying(160) NOT NULL,
    payment_id uuid,
    event_type character varying(80) NOT NULL,
    processing_status character varying(20) DEFAULT 'RECEIVED'::character varying NOT NULL,
    received_at timestamp with time zone DEFAULT now() NOT NULL,
    processed_at timestamp with time zone,
    CONSTRAINT payment_gateway_events_processing_status_check CHECK (((processing_status)::text = ANY ((ARRAY['RECEIVED'::character varying, 'PROCESSED'::character varying, 'IGNORED'::character varying, 'FAILED'::character varying])::text[])))
);


--
-- Name: payment_qr_sessions; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.payment_qr_sessions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    payment_id uuid NOT NULL,
    provider character varying(40) NOT NULL,
    provider_request_id character varying(150),
    qr_payload text NOT NULL,
    amount numeric(14,0) NOT NULL,
    status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone,
    CONSTRAINT ck_qr_expiration CHECK (((expires_at IS NULL) OR (expires_at > created_at))),
    CONSTRAINT payment_qr_sessions_amount_check CHECK ((amount > (0)::numeric)),
    CONSTRAINT payment_qr_sessions_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'EXPIRED'::character varying, 'SUPERSEDED'::character varying, 'CANCELLED'::character varying])::text[])))
);


--
-- Name: payments; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.payments (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    payment_code character varying(40) NOT NULL,
    visit_id uuid NOT NULL,
    amount numeric(14,0) NOT NULL,
    payment_method character varying(20) DEFAULT 'QR'::character varying NOT NULL,
    provider character varying(30),
    external_transaction_ref character varying(150),
    status character varying(20) DEFAULT 'PENDING'::character varying NOT NULL,
    paid_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_payment_method_v12 CHECK (((payment_method)::text = ANY ((ARRAY['QR'::character varying, 'CASH'::character varying])::text[]))),
    CONSTRAINT payments_amount_check CHECK ((amount > (0)::numeric)),
    CONSTRAINT payments_status_check CHECK (((status)::text = ANY ((ARRAY['PENDING'::character varying, 'PAID'::character varying, 'FAILED'::character varying, 'EXPIRED'::character varying, 'CANCELLED'::character varying])::text[])))
);


--
-- Name: permissions; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.permissions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    code character varying(100) NOT NULL,
    description text
);


--
-- Name: prescription_items; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.prescription_items (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    prescription_id uuid NOT NULL,
    medicine_id uuid NOT NULL,
    quantity integer NOT NULL,
    dosage character varying(255) NOT NULL,
    frequency character varying(255),
    duration_days integer,
    instructions text,
    CONSTRAINT prescription_items_duration_days_check CHECK ((duration_days > 0)),
    CONSTRAINT prescription_items_quantity_check CHECK ((quantity > 0))
);


--
-- Name: prescriptions; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.prescriptions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    prescription_code character varying(40) NOT NULL,
    visit_id uuid NOT NULL,
    doctor_id uuid NOT NULL,
    status character varying(20) DEFAULT 'DRAFT'::character varying NOT NULL,
    issued_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT prescriptions_status_check CHECK (((status)::text = ANY ((ARRAY['DRAFT'::character varying, 'ISSUED'::character varying, 'DISPENSED'::character varying, 'CANCELLED'::character varying])::text[])))
);


--
-- Name: queue_entries; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.queue_entries (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    queue_id uuid NOT NULL,
    visit_id uuid,
    appointment_id uuid,
    diagnostic_order_id uuid,
    queue_number integer NOT NULL,
    queue_class character varying(25) NOT NULL,
    priority integer DEFAULT 0 NOT NULL,
    scheduled_at timestamp with time zone,
    ready_at timestamp with time zone,
    called_at timestamp with time zone,
    service_started_at timestamp with time zone,
    service_ended_at timestamp with time zone,
    status character varying(25) DEFAULT 'RESERVED'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    presence_status character varying(25) DEFAULT 'UNKNOWN'::character varying NOT NULL,
    presence_verified_at timestamp with time zone,
    presence_verified_by uuid,
    priority_reason text,
    priority_approved_by uuid,
    priority_approved_at timestamp with time zone,
    CONSTRAINT ck_queue_presence CHECK (((presence_status)::text = ANY ((ARRAY['UNKNOWN'::character varying, 'AT_SERVICE_POINT'::character varying, 'LEFT_SERVICE_POINT'::character varying])::text[]))),
    CONSTRAINT queue_entries_check CHECK (((visit_id IS NOT NULL) OR (appointment_id IS NOT NULL))),
    CONSTRAINT queue_entries_queue_class_check CHECK (((queue_class)::text = ANY ((ARRAY['APPOINTMENT'::character varying, 'WALK_IN'::character varying, 'RETURN'::character varying, 'DIAGNOSTIC'::character varying])::text[]))),
    CONSTRAINT queue_entries_queue_number_check CHECK ((queue_number > 0)),
    CONSTRAINT queue_entries_status_check CHECK (((status)::text = ANY ((ARRAY['RESERVED'::character varying, 'CHECKED_IN'::character varying, 'READY'::character varying, 'CALLED'::character varying, 'IN_SERVICE'::character varying, 'COMPLETED'::character varying, 'BLOCKED'::character varying, 'PASSED'::character varying, 'NO_SHOW'::character varying, 'CANCELLED'::character varying, 'EXPIRED'::character varying])::text[])))
);


--
-- Name: COLUMN queue_entries.priority; Type: COMMENT; Schema: medflow; Owner: -
--

COMMENT ON COLUMN medflow.queue_entries.priority IS 'Queue-engine priority. Clinical override requires authorization and reason in the domain service.';


--
-- Name: queue_events; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.queue_events (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    queue_entry_id uuid NOT NULL,
    event_type character varying(40) NOT NULL,
    old_status character varying(25),
    new_status character varying(25),
    reason text,
    performed_by uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: queue_policies; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.queue_policies (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hospital_id uuid NOT NULL,
    version integer NOT NULL,
    config jsonb DEFAULT '{}'::jsonb NOT NULL,
    effective_from timestamp with time zone NOT NULL,
    effective_to timestamp with time zone,
    status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    CONSTRAINT queue_policies_check CHECK (((effective_to IS NULL) OR (effective_to > effective_from))),
    CONSTRAINT queue_policies_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'INACTIVE'::character varying])::text[]))),
    CONSTRAINT queue_policies_version_check CHECK ((version > 0))
);


--
-- Name: queues; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.queues (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hospital_id uuid NOT NULL,
    service_point_id uuid NOT NULL,
    service_date date NOT NULL,
    queue_type character varying(30) NOT NULL,
    status character varying(20) DEFAULT 'OPEN'::character varying NOT NULL,
    CONSTRAINT queues_queue_type_check CHECK (((queue_type)::text = ANY ((ARRAY['DOCTOR'::character varying, 'DIAGNOSTIC'::character varying])::text[]))),
    CONSTRAINT queues_status_check CHECK (((status)::text = ANY ((ARRAY['OPEN'::character varying, 'CLOSED'::character varying])::text[])))
);


--
-- Name: refresh_tokens; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.refresh_tokens (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    token_hash character varying(128) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    replaced_by_token_hash character varying(128),
    created_by_ip character varying(45),
    revoked_by_ip character varying(45),
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: role_permissions; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.role_permissions (
    role_id uuid NOT NULL,
    permission_id uuid NOT NULL
);


--
-- Name: roles; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.roles (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    code character varying(60) NOT NULL,
    name character varying(120) NOT NULL,
    is_active boolean DEFAULT true NOT NULL
);


--
-- Name: rooms; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.rooms (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hospital_id uuid NOT NULL,
    department_id uuid NOT NULL,
    room_code character varying(40) NOT NULL,
    name character varying(150) NOT NULL,
    location character varying(255),
    capacity integer DEFAULT 1 NOT NULL,
    status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    CONSTRAINT rooms_capacity_check CHECK ((capacity > 0)),
    CONSTRAINT rooms_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'INACTIVE'::character varying, 'UNAVAILABLE'::character varying])::text[])))
);


--
-- Name: scan_events; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.scan_events (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    scannable_code_id uuid,
    scan_purpose character varying(30) NOT NULL,
    result character varying(24) NOT NULL,
    scanned_by uuid,
    service_point_id uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT scan_events_result_check CHECK (((result)::text = ANY ((ARRAY['RESOLVED'::character varying, 'REJECTED'::character varying, 'EXPIRED'::character varying, 'REVOKED'::character varying, 'NOT_FOUND'::character varying])::text[]))),
    CONSTRAINT scan_events_scan_purpose_check CHECK (((scan_purpose)::text = ANY ((ARRAY['PATIENT_LOOKUP'::character varying, 'APPOINTMENT_CHECKIN'::character varying, 'DIAGNOSTIC_INTAKE'::character varying, 'QUEUE_LOOKUP'::character varying])::text[])))
);


--
-- Name: scannable_codes; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.scannable_codes (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    code_type character varying(32) NOT NULL,
    token_hash bytea NOT NULL,
    patient_id uuid,
    appointment_id uuid,
    diagnostic_order_id uuid,
    queue_entry_id uuid,
    status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    issued_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone,
    used_at timestamp with time zone,
    issued_by uuid,
    revoked_at timestamp with time zone,
    CONSTRAINT ck_code_dates CHECK (((expires_at IS NULL) OR (expires_at > issued_at))),
    CONSTRAINT ck_code_target CHECK (((((code_type)::text = 'PATIENT_CARD'::text) AND (patient_id IS NOT NULL) AND (appointment_id IS NULL) AND (diagnostic_order_id IS NULL) AND (queue_entry_id IS NULL)) OR (((code_type)::text = 'APPOINTMENT_CHECKIN'::text) AND (appointment_id IS NOT NULL) AND (patient_id IS NULL) AND (diagnostic_order_id IS NULL) AND (queue_entry_id IS NULL)) OR (((code_type)::text = 'DIAGNOSTIC_TICKET'::text) AND (diagnostic_order_id IS NOT NULL) AND (patient_id IS NULL) AND (appointment_id IS NULL) AND (queue_entry_id IS NULL)) OR (((code_type)::text = 'QUEUE_TICKET'::text) AND (queue_entry_id IS NOT NULL) AND (patient_id IS NULL) AND (appointment_id IS NULL) AND (diagnostic_order_id IS NULL)))),
    CONSTRAINT ck_used_code CHECK ((((status)::text <> 'USED'::text) OR (used_at IS NOT NULL))),
    CONSTRAINT scannable_codes_code_type_check CHECK (((code_type)::text = ANY ((ARRAY['PATIENT_CARD'::character varying, 'APPOINTMENT_CHECKIN'::character varying, 'DIAGNOSTIC_TICKET'::character varying, 'QUEUE_TICKET'::character varying])::text[]))),
    CONSTRAINT scannable_codes_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'USED'::character varying, 'REVOKED'::character varying])::text[]))),
    CONSTRAINT scannable_codes_token_hash_check CHECK ((octet_length(token_hash) = 32))
);


--
-- Name: service_points; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.service_points (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hospital_id uuid NOT NULL,
    department_id uuid,
    room_id uuid,
    service_id uuid,
    name character varying(150) NOT NULL,
    point_type character varying(20) NOT NULL,
    status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    CONSTRAINT service_points_point_type_check CHECK (((point_type)::text = ANY ((ARRAY['DOCTOR'::character varying, 'DIAGNOSTIC'::character varying])::text[]))),
    CONSTRAINT service_points_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'INACTIVE'::character varying])::text[])))
);


--
-- Name: service_prices; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.service_prices (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    service_id uuid NOT NULL,
    price numeric(14,0) NOT NULL,
    effective_from timestamp with time zone NOT NULL,
    effective_to timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT service_prices_check CHECK (((effective_to IS NULL) OR (effective_to > effective_from))),
    CONSTRAINT service_prices_price_check CHECK ((price >= (0)::numeric))
);


--
-- Name: services; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.services (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hospital_id uuid NOT NULL,
    department_id uuid NOT NULL,
    service_code character varying(40) NOT NULL,
    name character varying(255) NOT NULL,
    service_type character varying(20) NOT NULL,
    description text,
    duration_minutes integer,
    online_bookable boolean DEFAULT false NOT NULL,
    status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    CONSTRAINT services_duration_minutes_check CHECK ((duration_minutes > 0)),
    CONSTRAINT services_service_type_check CHECK (((service_type)::text = ANY ((ARRAY['EXAMINATION'::character varying, 'DIAGNOSTIC'::character varying])::text[]))),
    CONSTRAINT services_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'INACTIVE'::character varying])::text[])))
);


--
-- Name: staff; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.staff (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hospital_id uuid NOT NULL,
    department_id uuid,
    user_id uuid NOT NULL,
    staff_code character varying(40) NOT NULL,
    full_name character varying(255) NOT NULL,
    job_position character varying(100),
    phone character varying(30),
    email character varying(255),
    status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    avatar_url text,
    gender character varying(20),
    CONSTRAINT staff_status_check CHECK (((status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'INACTIVE'::character varying])::text[])))
);


--
-- Name: stock_transactions; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.stock_transactions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    medicine_batch_id uuid NOT NULL,
    transaction_type character varying(20) NOT NULL,
    quantity_change integer NOT NULL,
    quantity_before integer NOT NULL,
    quantity_after integer NOT NULL,
    dispense_item_id uuid,
    reason text,
    status character varying(20) DEFAULT 'POSTED'::character varying NOT NULL,
    performed_by uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT stock_transactions_check CHECK ((quantity_after = (quantity_before + quantity_change))),
    CONSTRAINT stock_transactions_quantity_after_check CHECK ((quantity_after >= 0)),
    CONSTRAINT stock_transactions_quantity_before_check CHECK ((quantity_before >= 0)),
    CONSTRAINT stock_transactions_quantity_change_check CHECK ((quantity_change <> 0)),
    CONSTRAINT stock_transactions_status_check CHECK (((status)::text = ANY ((ARRAY['POSTED'::character varying, 'VOIDED'::character varying])::text[]))),
    CONSTRAINT stock_transactions_transaction_type_check CHECK (((transaction_type)::text = ANY ((ARRAY['RECEIPT'::character varying, 'DISPENSE'::character varying, 'ADJUSTMENT'::character varying, 'REVERSAL'::character varying])::text[])))
);


--
-- Name: user_roles; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.user_roles (
    user_id uuid NOT NULL,
    role_id uuid NOT NULL
);


--
-- Name: users; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.users (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    username character varying(80) NOT NULL,
    email character varying(255) NOT NULL,
    password_hash text,
    auth_provider character varying(30) DEFAULT 'LOCAL'::character varying NOT NULL,
    provider_subject character varying(255),
    account_status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    email_verified_at timestamp with time zone,
    last_login_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    failed_login_attempts integer DEFAULT 0 NOT NULL,
    locked_until timestamp with time zone,
    CONSTRAINT ck_local_password CHECK ((((auth_provider)::text <> 'LOCAL'::text) OR (password_hash IS NOT NULL))),
    CONSTRAINT users_account_status_check CHECK (((account_status)::text = ANY ((ARRAY['ACTIVE'::character varying, 'INACTIVE'::character varying, 'LOCKED'::character varying, 'PENDING'::character varying])::text[]))),
    CONSTRAINT users_failed_login_attempts_check CHECK ((failed_login_attempts >= 0))
);


--
-- Name: visit_assignment_history; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.visit_assignment_history (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    visit_id uuid NOT NULL,
    doctor_id uuid,
    room_id uuid,
    assigned_by uuid,
    reason text,
    assigned_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: visit_charges; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.visit_charges (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    visit_id uuid NOT NULL,
    service_id uuid,
    description character varying(255) NOT NULL,
    source_type character varying(30),
    source_id uuid,
    quantity integer DEFAULT 1 NOT NULL,
    unit_price numeric(14,0) NOT NULL,
    amount numeric(14,0) GENERATED ALWAYS AS (((quantity)::numeric * unit_price)) STORED,
    status character varying(20) DEFAULT 'PAYABLE'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    prescription_item_id uuid,
    CONSTRAINT ck_visit_charge_medication_link CHECK ((((source_type)::text <> 'MEDICATION'::text) OR ((prescription_item_id IS NOT NULL) AND (service_id IS NULL)))),
    CONSTRAINT ck_visit_charge_source_v12 CHECK (((source_type IS NULL) OR ((source_type)::text = ANY ((ARRAY['EXAMINATION_SERVICE'::character varying, 'DIAGNOSTIC_SERVICE'::character varying, 'MEDICATION'::character varying])::text[])))),
    CONSTRAINT visit_charges_quantity_check CHECK ((quantity > 0)),
    CONSTRAINT visit_charges_status_check CHECK (((status)::text = ANY ((ARRAY['PAYABLE'::character varying, 'VOIDED'::character varying])::text[]))),
    CONSTRAINT visit_charges_unit_price_check CHECK ((unit_price >= (0)::numeric))
);


--
-- Name: visit_diagnoses; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.visit_diagnoses (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    visit_id uuid NOT NULL,
    diagnosis_code character varying(30),
    diagnosis_name character varying(255) NOT NULL,
    diagnosis_type character varying(20) DEFAULT 'SECONDARY'::character varying NOT NULL,
    notes text,
    recorded_by uuid,
    recorded_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT visit_diagnoses_diagnosis_type_check CHECK (((diagnosis_type)::text = ANY ((ARRAY['PRIMARY'::character varying, 'SECONDARY'::character varying])::text[])))
);


--
-- Name: visit_vitals; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.visit_vitals (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    visit_id uuid NOT NULL,
    recorded_by uuid,
    measured_at timestamp with time zone DEFAULT now() NOT NULL,
    systolic_bp smallint,
    diastolic_bp smallint,
    pulse_bpm smallint,
    respiratory_rate smallint,
    temperature_c numeric(4,1),
    spo2_percent numeric(5,2),
    height_cm numeric(5,1),
    weight_kg numeric(6,2),
    notes text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT visit_vitals_check CHECK ((((systolic_bp IS NULL) AND (diastolic_bp IS NULL)) OR ((systolic_bp IS NOT NULL) AND (diastolic_bp IS NOT NULL)))),
    CONSTRAINT visit_vitals_diastolic_bp_check CHECK (((diastolic_bp >= 1) AND (diastolic_bp <= 250))),
    CONSTRAINT visit_vitals_height_cm_check CHECK (((height_cm >= (20)::numeric) AND (height_cm <= (250)::numeric))),
    CONSTRAINT visit_vitals_pulse_bpm_check CHECK (((pulse_bpm >= 1) AND (pulse_bpm <= 300))),
    CONSTRAINT visit_vitals_respiratory_rate_check CHECK (((respiratory_rate >= 1) AND (respiratory_rate <= 150))),
    CONSTRAINT visit_vitals_spo2_percent_check CHECK (((spo2_percent >= (0)::numeric) AND (spo2_percent <= (100)::numeric))),
    CONSTRAINT visit_vitals_systolic_bp_check CHECK (((systolic_bp >= 1) AND (systolic_bp <= 350))),
    CONSTRAINT visit_vitals_temperature_c_check CHECK (((temperature_c >= (25)::numeric) AND (temperature_c <= (45)::numeric))),
    CONSTRAINT visit_vitals_weight_kg_check CHECK (((weight_kg > (0)::numeric) AND (weight_kg <= (500)::numeric)))
);


--
-- Name: TABLE visit_vitals; Type: COMMENT; Schema: medflow; Owner: -
--

COMMENT ON TABLE medflow.visit_vitals IS 'Each row is one observation set/time; BMI derived from height/weight by application, not duplicated in patients.';


--
-- Name: visits; Type: TABLE; Schema: medflow; Owner: -
--

CREATE TABLE medflow.visits (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    visit_code character varying(40) NOT NULL,
    hospital_id uuid NOT NULL,
    patient_id uuid NOT NULL,
    appointment_id uuid,
    assigned_doctor_id uuid,
    assigned_room_id uuid,
    check_in_at timestamp with time zone DEFAULT now() NOT NULL,
    reason_for_visit text,
    status character varying(30) DEFAULT 'CHECKED_IN'::character varying NOT NULL,
    completed_at timestamp with time zone,
    created_by uuid,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    arrived_at timestamp with time zone,
    mobility_support_notes text,
    CONSTRAINT visits_status_check CHECK (((status)::text = ANY ((ARRAY['CHECKED_IN'::character varying, 'WAITING'::character varying, 'IN_CONSULTATION'::character varying, 'AWAITING_DIAGNOSTIC'::character varying, 'RETURN_CONSULTATION'::character varying, 'COMPLETED'::character varying, 'CANCELLED'::character varying])::text[])))
);


--
-- Name: appointment_events appointment_events_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointment_events
    ADD CONSTRAINT appointment_events_pkey PRIMARY KEY (id);


--
-- Name: appointment_policies appointment_policies_hospital_id_version_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointment_policies
    ADD CONSTRAINT appointment_policies_hospital_id_version_key UNIQUE (hospital_id, version);


--
-- Name: appointment_policies appointment_policies_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointment_policies
    ADD CONSTRAINT appointment_policies_pkey PRIMARY KEY (id);


--
-- Name: appointments appointments_appointment_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointments
    ADD CONSTRAINT appointments_appointment_code_key UNIQUE (appointment_code);


--
-- Name: appointments appointments_id_hospital_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointments
    ADD CONSTRAINT appointments_id_hospital_id_key UNIQUE (id, hospital_id);


--
-- Name: appointments appointments_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointments
    ADD CONSTRAINT appointments_pkey PRIMARY KEY (id);


--
-- Name: audit_logs audit_logs_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.audit_logs
    ADD CONSTRAINT audit_logs_pkey PRIMARY KEY (id);


--
-- Name: auth_verification_challenges auth_verification_challenges_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.auth_verification_challenges
    ADD CONSTRAINT auth_verification_challenges_pkey PRIMARY KEY (id);


--
-- Name: departments departments_hospital_id_department_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.departments
    ADD CONSTRAINT departments_hospital_id_department_code_key UNIQUE (hospital_id, department_code);


--
-- Name: departments departments_id_hospital_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.departments
    ADD CONSTRAINT departments_id_hospital_id_key UNIQUE (id, hospital_id);


--
-- Name: departments departments_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.departments
    ADD CONSTRAINT departments_pkey PRIMARY KEY (id);


--
-- Name: diagnostic_orders diagnostic_orders_order_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.diagnostic_orders
    ADD CONSTRAINT diagnostic_orders_order_code_key UNIQUE (order_code);


--
-- Name: diagnostic_orders diagnostic_orders_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.diagnostic_orders
    ADD CONSTRAINT diagnostic_orders_pkey PRIMARY KEY (id);


--
-- Name: diagnostic_result_files diagnostic_result_files_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.diagnostic_result_files
    ADD CONSTRAINT diagnostic_result_files_pkey PRIMARY KEY (id);


--
-- Name: diagnostic_results diagnostic_results_diagnostic_order_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.diagnostic_results
    ADD CONSTRAINT diagnostic_results_diagnostic_order_id_key UNIQUE (diagnostic_order_id);


--
-- Name: diagnostic_results diagnostic_results_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.diagnostic_results
    ADD CONSTRAINT diagnostic_results_pkey PRIMARY KEY (id);


--
-- Name: dispense_batch_allocations dispense_batch_allocations_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.dispense_batch_allocations
    ADD CONSTRAINT dispense_batch_allocations_pkey PRIMARY KEY (dispense_item_id, medicine_batch_id);


--
-- Name: dispense_items dispense_items_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.dispense_items
    ADD CONSTRAINT dispense_items_pkey PRIMARY KEY (id);


--
-- Name: dispenses dispenses_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.dispenses
    ADD CONSTRAINT dispenses_pkey PRIMARY KEY (id);


--
-- Name: doctors doctors_doctor_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.doctors
    ADD CONSTRAINT doctors_doctor_code_key UNIQUE (doctor_code);


--
-- Name: doctors doctors_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.doctors
    ADD CONSTRAINT doctors_pkey PRIMARY KEY (id);


--
-- Name: doctors doctors_staff_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.doctors
    ADD CONSTRAINT doctors_staff_id_key UNIQUE (staff_id);


--
-- Name: appointment_policies ex_appointment_policy_period; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointment_policies
    ADD CONSTRAINT ex_appointment_policy_period EXCLUDE USING gist (hospital_id WITH =, tstzrange(effective_from, COALESCE(effective_to, 'infinity'::timestamp with time zone), '[)'::text) WITH &&) WHERE (((status)::text = 'ACTIVE'::text));


--
-- Name: examination_schedules ex_doctor_schedule_overlap; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.examination_schedules
    ADD CONSTRAINT ex_doctor_schedule_overlap EXCLUDE USING gist (doctor_id WITH =, tstzrange(starts_at, ends_at, '[)'::text) WITH &&) WHERE (((doctor_id IS NOT NULL) AND ((status)::text = ANY ((ARRAY['OPEN'::character varying, 'FULL'::character varying])::text[]))));


--
-- Name: medicine_prices ex_medicine_price_period; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medicine_prices
    ADD CONSTRAINT ex_medicine_price_period EXCLUDE USING gist (medicine_id WITH =, tstzrange(effective_from, COALESCE(effective_to, 'infinity'::timestamp with time zone), '[)'::text) WITH &&);


--
-- Name: appointments ex_patient_appointment_overlap; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointments
    ADD CONSTRAINT ex_patient_appointment_overlap EXCLUDE USING gist (patient_id WITH =, tstzrange(scheduled_start, scheduled_end, '[)'::text) WITH &&) WHERE (((status)::text = ANY ((ARRAY['BOOKED'::character varying, 'CHECKED_IN'::character varying])::text[])));


--
-- Name: queue_policies ex_queue_policy_period; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_policies
    ADD CONSTRAINT ex_queue_policy_period EXCLUDE USING gist (hospital_id WITH =, tstzrange(effective_from, COALESCE(effective_to, 'infinity'::timestamp with time zone), '[)'::text) WITH &&) WHERE (((status)::text = 'ACTIVE'::text));


--
-- Name: examination_schedules ex_room_schedule_overlap; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.examination_schedules
    ADD CONSTRAINT ex_room_schedule_overlap EXCLUDE USING gist (room_id WITH =, tstzrange(starts_at, ends_at, '[)'::text) WITH &&) WHERE (((room_id IS NOT NULL) AND ((status)::text = ANY ((ARRAY['OPEN'::character varying, 'FULL'::character varying])::text[]))));


--
-- Name: service_prices ex_service_price_period; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.service_prices
    ADD CONSTRAINT ex_service_price_period EXCLUDE USING gist (service_id WITH =, tstzrange(effective_from, COALESCE(effective_to, 'infinity'::timestamp with time zone), '[)'::text) WITH &&);


--
-- Name: examination_schedules examination_schedules_id_hospital_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.examination_schedules
    ADD CONSTRAINT examination_schedules_id_hospital_id_key UNIQUE (id, hospital_id);


--
-- Name: examination_schedules examination_schedules_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.examination_schedules
    ADD CONSTRAINT examination_schedules_pkey PRIMARY KEY (id);


--
-- Name: hospitals hospitals_hospital_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.hospitals
    ADD CONSTRAINT hospitals_hospital_code_key UNIQUE (hospital_code);


--
-- Name: hospitals hospitals_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.hospitals
    ADD CONSTRAINT hospitals_pkey PRIMARY KEY (id);


--
-- Name: identity_verifications identity_verifications_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.identity_verifications
    ADD CONSTRAINT identity_verifications_pkey PRIMARY KEY (id);


--
-- Name: identity_verifications identity_verifications_provider_provider_reference_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.identity_verifications
    ADD CONSTRAINT identity_verifications_provider_provider_reference_key UNIQUE (provider, provider_reference);


--
-- Name: integration_configs integration_configs_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.integration_configs
    ADD CONSTRAINT integration_configs_pkey PRIMARY KEY (id);


--
-- Name: medical_record_amendments medical_record_amendments_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medical_record_amendments
    ADD CONSTRAINT medical_record_amendments_pkey PRIMARY KEY (id);


--
-- Name: medical_records medical_records_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medical_records
    ADD CONSTRAINT medical_records_pkey PRIMARY KEY (id);


--
-- Name: medical_records medical_records_visit_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medical_records
    ADD CONSTRAINT medical_records_visit_id_key UNIQUE (visit_id);


--
-- Name: medicine_batches medicine_batches_medicine_id_batch_number_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medicine_batches
    ADD CONSTRAINT medicine_batches_medicine_id_batch_number_key UNIQUE (medicine_id, batch_number);


--
-- Name: medicine_batches medicine_batches_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medicine_batches
    ADD CONSTRAINT medicine_batches_pkey PRIMARY KEY (id);


--
-- Name: medicine_prices medicine_prices_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medicine_prices
    ADD CONSTRAINT medicine_prices_pkey PRIMARY KEY (id);


--
-- Name: medicines medicines_hospital_id_medicine_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medicines
    ADD CONSTRAINT medicines_hospital_id_medicine_code_key UNIQUE (hospital_id, medicine_code);


--
-- Name: medicines medicines_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medicines
    ADD CONSTRAINT medicines_pkey PRIMARY KEY (id);


--
-- Name: notifications notifications_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.notifications
    ADD CONSTRAINT notifications_pkey PRIMARY KEY (id);


--
-- Name: outbox_events outbox_events_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.outbox_events
    ADD CONSTRAINT outbox_events_pkey PRIMARY KEY (id);


--
-- Name: patient_allergies patient_allergies_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patient_allergies
    ADD CONSTRAINT patient_allergies_pkey PRIMARY KEY (id);


--
-- Name: patient_medical_history_entries patient_medical_history_entries_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patient_medical_history_entries
    ADD CONSTRAINT patient_medical_history_entries_pkey PRIMARY KEY (id);


--
-- Name: patient_relationships patient_relationships_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patient_relationships
    ADD CONSTRAINT patient_relationships_pkey PRIMARY KEY (owner_patient_id, member_patient_id);


--
-- Name: patients patients_patient_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patients
    ADD CONSTRAINT patients_patient_code_key UNIQUE (patient_code);


--
-- Name: patients patients_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patients
    ADD CONSTRAINT patients_pkey PRIMARY KEY (id);


--
-- Name: patients patients_user_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patients
    ADD CONSTRAINT patients_user_id_key UNIQUE (user_id);


--
-- Name: payment_allocations payment_allocations_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.payment_allocations
    ADD CONSTRAINT payment_allocations_pkey PRIMARY KEY (payment_id, charge_id);


--
-- Name: payment_gateway_events payment_gateway_events_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.payment_gateway_events
    ADD CONSTRAINT payment_gateway_events_pkey PRIMARY KEY (id);


--
-- Name: payment_gateway_events payment_gateway_events_provider_provider_event_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.payment_gateway_events
    ADD CONSTRAINT payment_gateway_events_provider_provider_event_id_key UNIQUE (provider, provider_event_id);


--
-- Name: payment_qr_sessions payment_qr_sessions_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.payment_qr_sessions
    ADD CONSTRAINT payment_qr_sessions_pkey PRIMARY KEY (id);


--
-- Name: payments payments_external_transaction_ref_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.payments
    ADD CONSTRAINT payments_external_transaction_ref_key UNIQUE (external_transaction_ref);


--
-- Name: payments payments_payment_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.payments
    ADD CONSTRAINT payments_payment_code_key UNIQUE (payment_code);


--
-- Name: payments payments_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.payments
    ADD CONSTRAINT payments_pkey PRIMARY KEY (id);


--
-- Name: permissions permissions_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.permissions
    ADD CONSTRAINT permissions_code_key UNIQUE (code);


--
-- Name: permissions permissions_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.permissions
    ADD CONSTRAINT permissions_pkey PRIMARY KEY (id);


--
-- Name: prescription_items prescription_items_id_prescription_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.prescription_items
    ADD CONSTRAINT prescription_items_id_prescription_id_key UNIQUE (id, prescription_id);


--
-- Name: prescription_items prescription_items_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.prescription_items
    ADD CONSTRAINT prescription_items_pkey PRIMARY KEY (id);


--
-- Name: prescriptions prescriptions_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.prescriptions
    ADD CONSTRAINT prescriptions_pkey PRIMARY KEY (id);


--
-- Name: prescriptions prescriptions_prescription_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.prescriptions
    ADD CONSTRAINT prescriptions_prescription_code_key UNIQUE (prescription_code);


--
-- Name: queue_entries queue_entries_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_entries
    ADD CONSTRAINT queue_entries_pkey PRIMARY KEY (id);


--
-- Name: queue_entries queue_entries_queue_id_queue_number_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_entries
    ADD CONSTRAINT queue_entries_queue_id_queue_number_key UNIQUE (queue_id, queue_number);


--
-- Name: queue_events queue_events_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_events
    ADD CONSTRAINT queue_events_pkey PRIMARY KEY (id);


--
-- Name: queue_policies queue_policies_hospital_id_version_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_policies
    ADD CONSTRAINT queue_policies_hospital_id_version_key UNIQUE (hospital_id, version);


--
-- Name: queue_policies queue_policies_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_policies
    ADD CONSTRAINT queue_policies_pkey PRIMARY KEY (id);


--
-- Name: queues queues_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queues
    ADD CONSTRAINT queues_pkey PRIMARY KEY (id);


--
-- Name: queues queues_service_point_id_service_date_queue_type_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queues
    ADD CONSTRAINT queues_service_point_id_service_date_queue_type_key UNIQUE (service_point_id, service_date, queue_type);


--
-- Name: refresh_tokens refresh_tokens_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.refresh_tokens
    ADD CONSTRAINT refresh_tokens_pkey PRIMARY KEY (id);


--
-- Name: refresh_tokens refresh_tokens_token_hash_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.refresh_tokens
    ADD CONSTRAINT refresh_tokens_token_hash_key UNIQUE (token_hash);


--
-- Name: role_permissions role_permissions_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.role_permissions
    ADD CONSTRAINT role_permissions_pkey PRIMARY KEY (role_id, permission_id);


--
-- Name: roles roles_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.roles
    ADD CONSTRAINT roles_code_key UNIQUE (code);


--
-- Name: roles roles_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.roles
    ADD CONSTRAINT roles_pkey PRIMARY KEY (id);


--
-- Name: rooms rooms_hospital_id_room_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.rooms
    ADD CONSTRAINT rooms_hospital_id_room_code_key UNIQUE (hospital_id, room_code);


--
-- Name: rooms rooms_id_hospital_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.rooms
    ADD CONSTRAINT rooms_id_hospital_id_key UNIQUE (id, hospital_id);


--
-- Name: rooms rooms_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.rooms
    ADD CONSTRAINT rooms_pkey PRIMARY KEY (id);


--
-- Name: scan_events scan_events_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.scan_events
    ADD CONSTRAINT scan_events_pkey PRIMARY KEY (id);


--
-- Name: scannable_codes scannable_codes_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.scannable_codes
    ADD CONSTRAINT scannable_codes_pkey PRIMARY KEY (id);


--
-- Name: scannable_codes scannable_codes_token_hash_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.scannable_codes
    ADD CONSTRAINT scannable_codes_token_hash_key UNIQUE (token_hash);


--
-- Name: service_points service_points_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.service_points
    ADD CONSTRAINT service_points_pkey PRIMARY KEY (id);


--
-- Name: service_prices service_prices_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.service_prices
    ADD CONSTRAINT service_prices_pkey PRIMARY KEY (id);


--
-- Name: services services_hospital_id_service_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.services
    ADD CONSTRAINT services_hospital_id_service_code_key UNIQUE (hospital_id, service_code);


--
-- Name: services services_id_hospital_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.services
    ADD CONSTRAINT services_id_hospital_id_key UNIQUE (id, hospital_id);


--
-- Name: services services_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.services
    ADD CONSTRAINT services_pkey PRIMARY KEY (id);


--
-- Name: staff staff_hospital_id_staff_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.staff
    ADD CONSTRAINT staff_hospital_id_staff_code_key UNIQUE (hospital_id, staff_code);


--
-- Name: staff staff_id_hospital_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.staff
    ADD CONSTRAINT staff_id_hospital_id_key UNIQUE (id, hospital_id);


--
-- Name: staff staff_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.staff
    ADD CONSTRAINT staff_pkey PRIMARY KEY (id);


--
-- Name: staff staff_user_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.staff
    ADD CONSTRAINT staff_user_id_key UNIQUE (user_id);


--
-- Name: stock_transactions stock_transactions_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.stock_transactions
    ADD CONSTRAINT stock_transactions_pkey PRIMARY KEY (id);


--
-- Name: users uq_auth_identity; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.users
    ADD CONSTRAINT uq_auth_identity UNIQUE (auth_provider, provider_subject);


--
-- Name: payment_qr_sessions uq_qr_provider_request; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.payment_qr_sessions
    ADD CONSTRAINT uq_qr_provider_request UNIQUE (provider, provider_request_id);


--
-- Name: user_roles user_roles_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.user_roles
    ADD CONSTRAINT user_roles_pkey PRIMARY KEY (user_id, role_id);


--
-- Name: users users_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


--
-- Name: users users_username_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.users
    ADD CONSTRAINT users_username_key UNIQUE (username);


--
-- Name: visit_assignment_history visit_assignment_history_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_assignment_history
    ADD CONSTRAINT visit_assignment_history_pkey PRIMARY KEY (id);


--
-- Name: visit_charges visit_charges_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_charges
    ADD CONSTRAINT visit_charges_pkey PRIMARY KEY (id);


--
-- Name: visit_diagnoses visit_diagnoses_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_diagnoses
    ADD CONSTRAINT visit_diagnoses_pkey PRIMARY KEY (id);


--
-- Name: visit_vitals visit_vitals_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_vitals
    ADD CONSTRAINT visit_vitals_pkey PRIMARY KEY (id);


--
-- Name: visits visits_appointment_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visits
    ADD CONSTRAINT visits_appointment_id_key UNIQUE (appointment_id);


--
-- Name: visits visits_id_hospital_id_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visits
    ADD CONSTRAINT visits_id_hospital_id_key UNIQUE (id, hospital_id);


--
-- Name: visits visits_pkey; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visits
    ADD CONSTRAINT visits_pkey PRIMARY KEY (id);


--
-- Name: visits visits_visit_code_key; Type: CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visits
    ADD CONSTRAINT visits_visit_code_key UNIQUE (visit_code);


--
-- Name: idx_appointments_patient_time; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_appointments_patient_time ON medflow.appointments USING btree (patient_id, scheduled_start DESC);


--
-- Name: idx_appointments_schedule_status; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_appointments_schedule_status ON medflow.appointments USING btree (schedule_id, status);


--
-- Name: idx_audit_entity; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_audit_entity ON medflow.audit_logs USING btree (entity_type, entity_id, created_at DESC);


--
-- Name: idx_batches_fefo; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_batches_fefo ON medflow.medicine_batches USING btree (medicine_id, expiry_date) WHERE ((status)::text = 'AVAILABLE'::text);


--
-- Name: idx_codes_appointment; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_codes_appointment ON medflow.scannable_codes USING btree (appointment_id) WHERE (appointment_id IS NOT NULL);


--
-- Name: idx_codes_diagnostic; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_codes_diagnostic ON medflow.scannable_codes USING btree (diagnostic_order_id) WHERE (diagnostic_order_id IS NOT NULL);


--
-- Name: idx_codes_patient; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_codes_patient ON medflow.scannable_codes USING btree (patient_id) WHERE (patient_id IS NOT NULL);


--
-- Name: idx_codes_queue; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_codes_queue ON medflow.scannable_codes USING btree (queue_entry_id) WHERE (queue_entry_id IS NOT NULL);


--
-- Name: idx_diagnostic_orders_visit; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_diagnostic_orders_visit ON medflow.diagnostic_orders USING btree (visit_id, status);


--
-- Name: idx_gateway_event_payment; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_gateway_event_payment ON medflow.payment_gateway_events USING btree (payment_id, received_at DESC);


--
-- Name: idx_outbox_pending; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_outbox_pending ON medflow.outbox_events USING btree (status, next_attempt_at) WHERE ((status)::text = ANY ((ARRAY['PENDING'::character varying, 'FAILED'::character varying])::text[]));


--
-- Name: idx_payment_qr_sessions_payment; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_payment_qr_sessions_payment ON medflow.payment_qr_sessions USING btree (payment_id, created_at DESC);


--
-- Name: idx_payment_visit; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_payment_visit ON medflow.payments USING btree (visit_id, status);


--
-- Name: idx_queue_selection; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_queue_selection ON medflow.queue_entries USING btree (queue_id, status, priority DESC, ready_at);


--
-- Name: idx_queue_visit; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_queue_visit ON medflow.queue_entries USING btree (visit_id);


--
-- Name: idx_refresh_tokens_active; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_refresh_tokens_active ON medflow.refresh_tokens USING btree (user_id) WHERE (revoked_at IS NULL);


--
-- Name: idx_scan_events_actor; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_scan_events_actor ON medflow.scan_events USING btree (scanned_by, created_at DESC);


--
-- Name: idx_scan_events_code; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_scan_events_code ON medflow.scan_events USING btree (scannable_code_id, created_at DESC);


--
-- Name: idx_schedules_doctor; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_schedules_doctor ON medflow.examination_schedules USING btree (doctor_id, starts_at) WHERE (doctor_id IS NOT NULL);


--
-- Name: idx_schedules_lookup; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_schedules_lookup ON medflow.examination_schedules USING btree (service_id, starts_at, status);


--
-- Name: idx_schedules_room; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_schedules_room ON medflow.examination_schedules USING btree (room_id, starts_at) WHERE (room_id IS NOT NULL);


--
-- Name: idx_stock_batch_time; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_stock_batch_time ON medflow.stock_transactions USING btree (medicine_batch_id, created_at DESC);


--
-- Name: idx_visits_patient; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX idx_visits_patient ON medflow.visits USING btree (patient_id, check_in_at DESC);


--
-- Name: ix_allergies_patient; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX ix_allergies_patient ON medflow.patient_allergies USING btree (patient_id, status);


--
-- Name: ix_amendments_record; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX ix_amendments_record ON medflow.medical_record_amendments USING btree (medical_record_id, requested_at DESC);


--
-- Name: ix_auth_challenges_fingerprint; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX ix_auth_challenges_fingerprint ON medflow.auth_verification_challenges USING btree (recipient_fingerprint, purpose, created_at DESC);


--
-- Name: ix_auth_challenges_user; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX ix_auth_challenges_user ON medflow.auth_verification_challenges USING btree (user_id, created_at DESC);


--
-- Name: ix_home_departments; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX ix_home_departments ON medflow.departments USING btree (hospital_id, is_featured DESC, display_order, name) WHERE ((status)::text = 'ACTIVE'::text);


--
-- Name: ix_identity_verifications_patient; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX ix_identity_verifications_patient ON medflow.identity_verifications USING btree (patient_id, requested_at DESC);


--
-- Name: ix_medical_history_patient; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX ix_medical_history_patient ON medflow.patient_medical_history_entries USING btree (patient_id, history_type, recorded_at DESC);


--
-- Name: ix_medicine_price_lookup; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX ix_medicine_price_lookup ON medflow.medicine_prices USING btree (medicine_id, effective_from DESC);


--
-- Name: ix_queue_presence; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX ix_queue_presence ON medflow.queue_entries USING btree (queue_id, presence_status, status);


--
-- Name: ix_vitals_visit_time; Type: INDEX; Schema: medflow; Owner: -
--

CREATE INDEX ix_vitals_visit_time ON medflow.visit_vitals USING btree (visit_id, measured_at DESC);


--
-- Name: uq_active_appointment_qr; Type: INDEX; Schema: medflow; Owner: -
--

CREATE UNIQUE INDEX uq_active_appointment_qr ON medflow.scannable_codes USING btree (appointment_id) WHERE (((code_type)::text = 'APPOINTMENT_CHECKIN'::text) AND ((status)::text = 'ACTIVE'::text));


--
-- Name: uq_active_diagnostic_ticket; Type: INDEX; Schema: medflow; Owner: -
--

CREATE UNIQUE INDEX uq_active_diagnostic_ticket ON medflow.scannable_codes USING btree (diagnostic_order_id) WHERE (((code_type)::text = 'DIAGNOSTIC_TICKET'::text) AND ((status)::text = 'ACTIVE'::text));


--
-- Name: uq_active_medication_charge; Type: INDEX; Schema: medflow; Owner: -
--

CREATE UNIQUE INDEX uq_active_medication_charge ON medflow.visit_charges USING btree (prescription_item_id) WHERE ((prescription_item_id IS NOT NULL) AND ((status)::text = 'PAYABLE'::text));


--
-- Name: uq_active_patient_barcode; Type: INDEX; Schema: medflow; Owner: -
--

CREATE UNIQUE INDEX uq_active_patient_barcode ON medflow.scannable_codes USING btree (patient_id) WHERE (((code_type)::text = 'PATIENT_CARD'::text) AND ((status)::text = 'ACTIVE'::text));


--
-- Name: uq_active_queue_ticket; Type: INDEX; Schema: medflow; Owner: -
--

CREATE UNIQUE INDEX uq_active_queue_ticket ON medflow.scannable_codes USING btree (queue_entry_id) WHERE (((code_type)::text = 'QUEUE_TICKET'::text) AND ((status)::text = 'ACTIVE'::text));


--
-- Name: uq_active_service_charge_source; Type: INDEX; Schema: medflow; Owner: -
--

CREATE UNIQUE INDEX uq_active_service_charge_source ON medflow.visit_charges USING btree (visit_id, source_type, source_id) WHERE ((source_id IS NOT NULL) AND ((source_type)::text = ANY ((ARRAY['EXAMINATION_SERVICE'::character varying, 'DIAGNOSTIC_SERVICE'::character varying])::text[])) AND ((status)::text = 'PAYABLE'::text));


--
-- Name: uq_dispense_non_cancelled_prescription; Type: INDEX; Schema: medflow; Owner: -
--

CREATE UNIQUE INDEX uq_dispense_non_cancelled_prescription ON medflow.dispenses USING btree (prescription_id) WHERE ((status)::text <> 'CANCELLED'::text);


--
-- Name: uq_dispense_prescription_item; Type: INDEX; Schema: medflow; Owner: -
--

CREATE UNIQUE INDEX uq_dispense_prescription_item ON medflow.dispense_items USING btree (dispense_id, prescription_item_id);


--
-- Name: uq_integration_scope; Type: INDEX; Schema: medflow; Owner: -
--

CREATE UNIQUE INDEX uq_integration_scope ON medflow.integration_configs USING btree (integration_code, COALESCE(hospital_id, '00000000-0000-0000-0000-000000000000'::uuid));


--
-- Name: uq_patients_identification; Type: INDEX; Schema: medflow; Owner: -
--

CREATE UNIQUE INDEX uq_patients_identification ON medflow.patients USING btree (identification_no) WHERE (identification_no IS NOT NULL);


--
-- Name: uq_payment_active_qr; Type: INDEX; Schema: medflow; Owner: -
--

CREATE UNIQUE INDEX uq_payment_active_qr ON medflow.payment_qr_sessions USING btree (payment_id) WHERE ((status)::text = 'ACTIVE'::text);


--
-- Name: uq_primary_diagnosis_per_visit; Type: INDEX; Schema: medflow; Owner: -
--

CREATE UNIQUE INDEX uq_primary_diagnosis_per_visit ON medflow.visit_diagnoses USING btree (visit_id) WHERE ((diagnosis_type)::text = 'PRIMARY'::text);


--
-- Name: uq_users_email_lower; Type: INDEX; Schema: medflow; Owner: -
--

CREATE UNIQUE INDEX uq_users_email_lower ON medflow.users USING btree (lower((email)::text));


--
-- Name: medical_record_amendments trg_amendment_immutable; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_amendment_immutable BEFORE DELETE OR UPDATE ON medflow.medical_record_amendments FOR EACH ROW EXECUTE FUNCTION medflow.prevent_decided_amendment_change();


--
-- Name: medical_record_amendments trg_amendment_parent; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_amendment_parent BEFORE INSERT ON medflow.medical_record_amendments FOR EACH ROW EXECUTE FUNCTION medflow.guard_amendment_finalized_parent();


--
-- Name: diagnostic_orders trg_diagnostic_order_service; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_diagnostic_order_service BEFORE INSERT OR UPDATE ON medflow.diagnostic_orders FOR EACH ROW EXECUTE FUNCTION medflow.guard_diagnostic_order_service();


--
-- Name: dispense_batch_allocations trg_dispense_batch_medicine; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_dispense_batch_medicine BEFORE INSERT OR UPDATE ON medflow.dispense_batch_allocations FOR EACH ROW EXECUTE FUNCTION medflow.guard_dispense_batch_medicine();


--
-- Name: dispense_items trg_dispense_item_prescription; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_dispense_item_prescription BEFORE INSERT OR UPDATE ON medflow.dispense_items FOR EACH ROW EXECUTE FUNCTION medflow.guard_dispense_item_prescription();


--
-- Name: dispenses trg_dispense_patient; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_dispense_patient BEFORE INSERT OR UPDATE ON medflow.dispenses FOR EACH ROW EXECUTE FUNCTION medflow.guard_dispense_patient();


--
-- Name: dispenses trg_full_dispense_completion; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_full_dispense_completion BEFORE INSERT OR UPDATE ON medflow.dispenses FOR EACH ROW EXECUTE FUNCTION medflow.guard_full_dispense_completion();


--
-- Name: medical_records trg_medical_record_immutable; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_medical_record_immutable BEFORE DELETE OR UPDATE ON medflow.medical_records FOR EACH ROW EXECUTE FUNCTION medflow.prevent_finalized_medical_record_change();


--
-- Name: payment_allocations trg_payment_allocation_visit; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_payment_allocation_visit BEFORE INSERT OR UPDATE ON medflow.payment_allocations FOR EACH ROW EXECUTE FUNCTION medflow.guard_payment_allocation_visit();


--
-- Name: prescription_items trg_prescription_item_hospital; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_prescription_item_hospital BEFORE INSERT OR UPDATE ON medflow.prescription_items FOR EACH ROW EXECUTE FUNCTION medflow.guard_prescription_item_hospital();


--
-- Name: queue_entries trg_queue_entry_context; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_queue_entry_context BEFORE INSERT OR UPDATE ON medflow.queue_entries FOR EACH ROW EXECUTE FUNCTION medflow.guard_queue_entry_context();


--
-- Name: queue_entries trg_queue_number_immutable; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_queue_number_immutable BEFORE UPDATE ON medflow.queue_entries FOR EACH ROW EXECUTE FUNCTION medflow.prevent_queue_renumber();


--
-- Name: rooms trg_room_department; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_room_department BEFORE INSERT OR UPDATE ON medflow.rooms FOR EACH ROW EXECUTE FUNCTION medflow.guard_catalog_department();


--
-- Name: examination_schedules trg_schedule_context; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_schedule_context BEFORE INSERT OR UPDATE ON medflow.examination_schedules FOR EACH ROW EXECUTE FUNCTION medflow.guard_schedule_context();


--
-- Name: services trg_service_department; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_service_department BEFORE INSERT OR UPDATE ON medflow.services FOR EACH ROW EXECUTE FUNCTION medflow.guard_catalog_department();


--
-- Name: service_points trg_service_point_department; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_service_point_department BEFORE INSERT OR UPDATE ON medflow.service_points FOR EACH ROW EXECUTE FUNCTION medflow.guard_catalog_department();


--
-- Name: staff trg_staff_department; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_staff_department BEFORE INSERT OR UPDATE ON medflow.staff FOR EACH ROW EXECUTE FUNCTION medflow.guard_catalog_department();


--
-- Name: visit_charges trg_visit_charge_link; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_visit_charge_link BEFORE INSERT OR UPDATE ON medflow.visit_charges FOR EACH ROW EXECUTE FUNCTION medflow.guard_visit_charge_link();


--
-- Name: visits trg_visit_context; Type: TRIGGER; Schema: medflow; Owner: -
--

CREATE TRIGGER trg_visit_context BEFORE INSERT OR UPDATE ON medflow.visits FOR EACH ROW EXECUTE FUNCTION medflow.guard_visit_context();


--
-- Name: appointment_events appointment_events_actor_user_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointment_events
    ADD CONSTRAINT appointment_events_actor_user_id_fkey FOREIGN KEY (actor_user_id) REFERENCES medflow.users(id);


--
-- Name: appointment_events appointment_events_appointment_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointment_events
    ADD CONSTRAINT appointment_events_appointment_id_fkey FOREIGN KEY (appointment_id) REFERENCES medflow.appointments(id);


--
-- Name: appointment_events appointment_events_new_schedule_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointment_events
    ADD CONSTRAINT appointment_events_new_schedule_id_fkey FOREIGN KEY (new_schedule_id) REFERENCES medflow.examination_schedules(id);


--
-- Name: appointment_events appointment_events_old_schedule_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointment_events
    ADD CONSTRAINT appointment_events_old_schedule_id_fkey FOREIGN KEY (old_schedule_id) REFERENCES medflow.examination_schedules(id);


--
-- Name: appointment_policies appointment_policies_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointment_policies
    ADD CONSTRAINT appointment_policies_hospital_id_fkey FOREIGN KEY (hospital_id) REFERENCES medflow.hospitals(id);


--
-- Name: appointments appointments_created_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointments
    ADD CONSTRAINT appointments_created_by_fkey FOREIGN KEY (created_by) REFERENCES medflow.users(id);


--
-- Name: appointments appointments_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointments
    ADD CONSTRAINT appointments_hospital_id_fkey FOREIGN KEY (hospital_id) REFERENCES medflow.hospitals(id);


--
-- Name: appointments appointments_patient_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointments
    ADD CONSTRAINT appointments_patient_id_fkey FOREIGN KEY (patient_id) REFERENCES medflow.patients(id);


--
-- Name: appointments appointments_schedule_id_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.appointments
    ADD CONSTRAINT appointments_schedule_id_hospital_id_fkey FOREIGN KEY (schedule_id, hospital_id) REFERENCES medflow.examination_schedules(id, hospital_id);


--
-- Name: audit_logs audit_logs_actor_user_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.audit_logs
    ADD CONSTRAINT audit_logs_actor_user_id_fkey FOREIGN KEY (actor_user_id) REFERENCES medflow.users(id);


--
-- Name: auth_verification_challenges auth_verification_challenges_user_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.auth_verification_challenges
    ADD CONSTRAINT auth_verification_challenges_user_id_fkey FOREIGN KEY (user_id) REFERENCES medflow.users(id) ON DELETE SET NULL;


--
-- Name: departments departments_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.departments
    ADD CONSTRAINT departments_hospital_id_fkey FOREIGN KEY (hospital_id) REFERENCES medflow.hospitals(id);


--
-- Name: diagnostic_orders diagnostic_orders_confirmed_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.diagnostic_orders
    ADD CONSTRAINT diagnostic_orders_confirmed_by_fkey FOREIGN KEY (confirmed_by) REFERENCES medflow.staff(id);


--
-- Name: diagnostic_orders diagnostic_orders_ordering_doctor_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.diagnostic_orders
    ADD CONSTRAINT diagnostic_orders_ordering_doctor_id_fkey FOREIGN KEY (ordering_doctor_id) REFERENCES medflow.doctors(id);


--
-- Name: diagnostic_orders diagnostic_orders_service_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.diagnostic_orders
    ADD CONSTRAINT diagnostic_orders_service_id_fkey FOREIGN KEY (service_id) REFERENCES medflow.services(id);


--
-- Name: diagnostic_orders diagnostic_orders_visit_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.diagnostic_orders
    ADD CONSTRAINT diagnostic_orders_visit_id_fkey FOREIGN KEY (visit_id) REFERENCES medflow.visits(id);


--
-- Name: diagnostic_result_files diagnostic_result_files_diagnostic_result_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.diagnostic_result_files
    ADD CONSTRAINT diagnostic_result_files_diagnostic_result_id_fkey FOREIGN KEY (diagnostic_result_id) REFERENCES medflow.diagnostic_results(id);


--
-- Name: diagnostic_results diagnostic_results_diagnostic_order_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.diagnostic_results
    ADD CONSTRAINT diagnostic_results_diagnostic_order_id_fkey FOREIGN KEY (diagnostic_order_id) REFERENCES medflow.diagnostic_orders(id);


--
-- Name: diagnostic_results diagnostic_results_performed_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.diagnostic_results
    ADD CONSTRAINT diagnostic_results_performed_by_fkey FOREIGN KEY (performed_by) REFERENCES medflow.staff(id);


--
-- Name: dispense_batch_allocations dispense_batch_allocations_dispense_item_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.dispense_batch_allocations
    ADD CONSTRAINT dispense_batch_allocations_dispense_item_id_fkey FOREIGN KEY (dispense_item_id) REFERENCES medflow.dispense_items(id);


--
-- Name: dispense_batch_allocations dispense_batch_allocations_medicine_batch_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.dispense_batch_allocations
    ADD CONSTRAINT dispense_batch_allocations_medicine_batch_id_fkey FOREIGN KEY (medicine_batch_id) REFERENCES medflow.medicine_batches(id);


--
-- Name: dispense_items dispense_items_dispense_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.dispense_items
    ADD CONSTRAINT dispense_items_dispense_id_fkey FOREIGN KEY (dispense_id) REFERENCES medflow.dispenses(id);


--
-- Name: dispense_items dispense_items_prescription_item_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.dispense_items
    ADD CONSTRAINT dispense_items_prescription_item_id_fkey FOREIGN KEY (prescription_item_id) REFERENCES medflow.prescription_items(id);


--
-- Name: dispenses dispenses_handed_over_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.dispenses
    ADD CONSTRAINT dispenses_handed_over_by_fkey FOREIGN KEY (handed_over_by) REFERENCES medflow.staff(id);


--
-- Name: dispenses dispenses_patient_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.dispenses
    ADD CONSTRAINT dispenses_patient_id_fkey FOREIGN KEY (patient_id) REFERENCES medflow.patients(id);


--
-- Name: dispenses dispenses_pharmacy_staff_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.dispenses
    ADD CONSTRAINT dispenses_pharmacy_staff_id_fkey FOREIGN KEY (pharmacy_staff_id) REFERENCES medflow.staff(id);


--
-- Name: dispenses dispenses_prepared_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.dispenses
    ADD CONSTRAINT dispenses_prepared_by_fkey FOREIGN KEY (prepared_by) REFERENCES medflow.staff(id);


--
-- Name: dispenses dispenses_prescription_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.dispenses
    ADD CONSTRAINT dispenses_prescription_id_fkey FOREIGN KEY (prescription_id) REFERENCES medflow.prescriptions(id);


--
-- Name: doctors doctors_staff_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.doctors
    ADD CONSTRAINT doctors_staff_id_fkey FOREIGN KEY (staff_id) REFERENCES medflow.staff(id);


--
-- Name: examination_schedules examination_schedules_doctor_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.examination_schedules
    ADD CONSTRAINT examination_schedules_doctor_id_fkey FOREIGN KEY (doctor_id) REFERENCES medflow.doctors(id);


--
-- Name: examination_schedules examination_schedules_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.examination_schedules
    ADD CONSTRAINT examination_schedules_hospital_id_fkey FOREIGN KEY (hospital_id) REFERENCES medflow.hospitals(id);


--
-- Name: examination_schedules examination_schedules_room_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.examination_schedules
    ADD CONSTRAINT examination_schedules_room_id_fkey FOREIGN KEY (room_id) REFERENCES medflow.rooms(id);


--
-- Name: examination_schedules examination_schedules_service_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.examination_schedules
    ADD CONSTRAINT examination_schedules_service_id_fkey FOREIGN KEY (service_id) REFERENCES medflow.services(id);


--
-- Name: queue_entries fk_queue_diagnostic_order; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_entries
    ADD CONSTRAINT fk_queue_diagnostic_order FOREIGN KEY (diagnostic_order_id) REFERENCES medflow.diagnostic_orders(id);


--
-- Name: identity_verifications identity_verifications_patient_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.identity_verifications
    ADD CONSTRAINT identity_verifications_patient_id_fkey FOREIGN KEY (patient_id) REFERENCES medflow.patients(id);


--
-- Name: identity_verifications identity_verifications_requested_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.identity_verifications
    ADD CONSTRAINT identity_verifications_requested_by_fkey FOREIGN KEY (requested_by) REFERENCES medflow.users(id);


--
-- Name: identity_verifications identity_verifications_verified_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.identity_verifications
    ADD CONSTRAINT identity_verifications_verified_by_fkey FOREIGN KEY (verified_by) REFERENCES medflow.users(id);


--
-- Name: integration_configs integration_configs_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.integration_configs
    ADD CONSTRAINT integration_configs_hospital_id_fkey FOREIGN KEY (hospital_id) REFERENCES medflow.hospitals(id);


--
-- Name: medical_record_amendments medical_record_amendments_approved_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medical_record_amendments
    ADD CONSTRAINT medical_record_amendments_approved_by_fkey FOREIGN KEY (approved_by) REFERENCES medflow.users(id);


--
-- Name: medical_record_amendments medical_record_amendments_medical_record_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medical_record_amendments
    ADD CONSTRAINT medical_record_amendments_medical_record_id_fkey FOREIGN KEY (medical_record_id) REFERENCES medflow.medical_records(id);


--
-- Name: medical_record_amendments medical_record_amendments_requested_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medical_record_amendments
    ADD CONSTRAINT medical_record_amendments_requested_by_fkey FOREIGN KEY (requested_by) REFERENCES medflow.users(id);


--
-- Name: medical_records medical_records_finalized_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medical_records
    ADD CONSTRAINT medical_records_finalized_by_fkey FOREIGN KEY (finalized_by) REFERENCES medflow.doctors(id);


--
-- Name: medical_records medical_records_visit_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medical_records
    ADD CONSTRAINT medical_records_visit_id_fkey FOREIGN KEY (visit_id) REFERENCES medflow.visits(id);


--
-- Name: medicine_batches medicine_batches_medicine_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medicine_batches
    ADD CONSTRAINT medicine_batches_medicine_id_fkey FOREIGN KEY (medicine_id) REFERENCES medflow.medicines(id);


--
-- Name: medicine_prices medicine_prices_created_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medicine_prices
    ADD CONSTRAINT medicine_prices_created_by_fkey FOREIGN KEY (created_by) REFERENCES medflow.users(id);


--
-- Name: medicine_prices medicine_prices_medicine_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medicine_prices
    ADD CONSTRAINT medicine_prices_medicine_id_fkey FOREIGN KEY (medicine_id) REFERENCES medflow.medicines(id);


--
-- Name: medicines medicines_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.medicines
    ADD CONSTRAINT medicines_hospital_id_fkey FOREIGN KEY (hospital_id) REFERENCES medflow.hospitals(id);


--
-- Name: notifications notifications_patient_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.notifications
    ADD CONSTRAINT notifications_patient_id_fkey FOREIGN KEY (patient_id) REFERENCES medflow.patients(id);


--
-- Name: notifications notifications_user_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.notifications
    ADD CONSTRAINT notifications_user_id_fkey FOREIGN KEY (user_id) REFERENCES medflow.users(id);


--
-- Name: patient_allergies patient_allergies_patient_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patient_allergies
    ADD CONSTRAINT patient_allergies_patient_id_fkey FOREIGN KEY (patient_id) REFERENCES medflow.patients(id);


--
-- Name: patient_allergies patient_allergies_updated_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patient_allergies
    ADD CONSTRAINT patient_allergies_updated_by_fkey FOREIGN KEY (updated_by) REFERENCES medflow.users(id);


--
-- Name: patient_medical_history_entries patient_medical_history_entries_patient_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patient_medical_history_entries
    ADD CONSTRAINT patient_medical_history_entries_patient_id_fkey FOREIGN KEY (patient_id) REFERENCES medflow.patients(id);


--
-- Name: patient_medical_history_entries patient_medical_history_entries_recorded_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patient_medical_history_entries
    ADD CONSTRAINT patient_medical_history_entries_recorded_by_fkey FOREIGN KEY (recorded_by) REFERENCES medflow.users(id);


--
-- Name: patient_medical_history_entries patient_medical_history_entries_source_visit_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patient_medical_history_entries
    ADD CONSTRAINT patient_medical_history_entries_source_visit_id_fkey FOREIGN KEY (source_visit_id) REFERENCES medflow.visits(id);


--
-- Name: patient_relationships patient_relationships_consent_verified_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patient_relationships
    ADD CONSTRAINT patient_relationships_consent_verified_by_fkey FOREIGN KEY (consent_verified_by) REFERENCES medflow.users(id);


--
-- Name: patient_relationships patient_relationships_member_patient_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patient_relationships
    ADD CONSTRAINT patient_relationships_member_patient_id_fkey FOREIGN KEY (member_patient_id) REFERENCES medflow.patients(id);


--
-- Name: patient_relationships patient_relationships_owner_patient_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patient_relationships
    ADD CONSTRAINT patient_relationships_owner_patient_id_fkey FOREIGN KEY (owner_patient_id) REFERENCES medflow.patients(id);


--
-- Name: patients patients_user_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.patients
    ADD CONSTRAINT patients_user_id_fkey FOREIGN KEY (user_id) REFERENCES medflow.users(id) ON DELETE SET NULL;


--
-- Name: payment_allocations payment_allocations_charge_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.payment_allocations
    ADD CONSTRAINT payment_allocations_charge_id_fkey FOREIGN KEY (charge_id) REFERENCES medflow.visit_charges(id);


--
-- Name: payment_allocations payment_allocations_payment_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.payment_allocations
    ADD CONSTRAINT payment_allocations_payment_id_fkey FOREIGN KEY (payment_id) REFERENCES medflow.payments(id);


--
-- Name: payment_gateway_events payment_gateway_events_payment_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.payment_gateway_events
    ADD CONSTRAINT payment_gateway_events_payment_id_fkey FOREIGN KEY (payment_id) REFERENCES medflow.payments(id);


--
-- Name: payment_qr_sessions payment_qr_sessions_payment_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.payment_qr_sessions
    ADD CONSTRAINT payment_qr_sessions_payment_id_fkey FOREIGN KEY (payment_id) REFERENCES medflow.payments(id);


--
-- Name: payments payments_visit_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.payments
    ADD CONSTRAINT payments_visit_id_fkey FOREIGN KEY (visit_id) REFERENCES medflow.visits(id);


--
-- Name: prescription_items prescription_items_medicine_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.prescription_items
    ADD CONSTRAINT prescription_items_medicine_id_fkey FOREIGN KEY (medicine_id) REFERENCES medflow.medicines(id);


--
-- Name: prescription_items prescription_items_prescription_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.prescription_items
    ADD CONSTRAINT prescription_items_prescription_id_fkey FOREIGN KEY (prescription_id) REFERENCES medflow.prescriptions(id);


--
-- Name: prescriptions prescriptions_doctor_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.prescriptions
    ADD CONSTRAINT prescriptions_doctor_id_fkey FOREIGN KEY (doctor_id) REFERENCES medflow.doctors(id);


--
-- Name: prescriptions prescriptions_visit_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.prescriptions
    ADD CONSTRAINT prescriptions_visit_id_fkey FOREIGN KEY (visit_id) REFERENCES medflow.visits(id);


--
-- Name: queue_entries queue_entries_appointment_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_entries
    ADD CONSTRAINT queue_entries_appointment_id_fkey FOREIGN KEY (appointment_id) REFERENCES medflow.appointments(id);


--
-- Name: queue_entries queue_entries_presence_verified_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_entries
    ADD CONSTRAINT queue_entries_presence_verified_by_fkey FOREIGN KEY (presence_verified_by) REFERENCES medflow.users(id);


--
-- Name: queue_entries queue_entries_priority_approved_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_entries
    ADD CONSTRAINT queue_entries_priority_approved_by_fkey FOREIGN KEY (priority_approved_by) REFERENCES medflow.users(id);


--
-- Name: queue_entries queue_entries_queue_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_entries
    ADD CONSTRAINT queue_entries_queue_id_fkey FOREIGN KEY (queue_id) REFERENCES medflow.queues(id);


--
-- Name: queue_entries queue_entries_visit_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_entries
    ADD CONSTRAINT queue_entries_visit_id_fkey FOREIGN KEY (visit_id) REFERENCES medflow.visits(id);


--
-- Name: queue_events queue_events_performed_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_events
    ADD CONSTRAINT queue_events_performed_by_fkey FOREIGN KEY (performed_by) REFERENCES medflow.users(id);


--
-- Name: queue_events queue_events_queue_entry_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_events
    ADD CONSTRAINT queue_events_queue_entry_id_fkey FOREIGN KEY (queue_entry_id) REFERENCES medflow.queue_entries(id);


--
-- Name: queue_policies queue_policies_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queue_policies
    ADD CONSTRAINT queue_policies_hospital_id_fkey FOREIGN KEY (hospital_id) REFERENCES medflow.hospitals(id);


--
-- Name: queues queues_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queues
    ADD CONSTRAINT queues_hospital_id_fkey FOREIGN KEY (hospital_id) REFERENCES medflow.hospitals(id);


--
-- Name: queues queues_service_point_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.queues
    ADD CONSTRAINT queues_service_point_id_fkey FOREIGN KEY (service_point_id) REFERENCES medflow.service_points(id);


--
-- Name: refresh_tokens refresh_tokens_user_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.refresh_tokens
    ADD CONSTRAINT refresh_tokens_user_id_fkey FOREIGN KEY (user_id) REFERENCES medflow.users(id) ON DELETE CASCADE;


--
-- Name: role_permissions role_permissions_permission_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.role_permissions
    ADD CONSTRAINT role_permissions_permission_id_fkey FOREIGN KEY (permission_id) REFERENCES medflow.permissions(id) ON DELETE CASCADE;


--
-- Name: role_permissions role_permissions_role_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.role_permissions
    ADD CONSTRAINT role_permissions_role_id_fkey FOREIGN KEY (role_id) REFERENCES medflow.roles(id) ON DELETE CASCADE;


--
-- Name: rooms rooms_department_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.rooms
    ADD CONSTRAINT rooms_department_id_fkey FOREIGN KEY (department_id) REFERENCES medflow.departments(id);


--
-- Name: rooms rooms_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.rooms
    ADD CONSTRAINT rooms_hospital_id_fkey FOREIGN KEY (hospital_id) REFERENCES medflow.hospitals(id);


--
-- Name: scan_events scan_events_scannable_code_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.scan_events
    ADD CONSTRAINT scan_events_scannable_code_id_fkey FOREIGN KEY (scannable_code_id) REFERENCES medflow.scannable_codes(id);


--
-- Name: scan_events scan_events_scanned_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.scan_events
    ADD CONSTRAINT scan_events_scanned_by_fkey FOREIGN KEY (scanned_by) REFERENCES medflow.users(id);


--
-- Name: scan_events scan_events_service_point_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.scan_events
    ADD CONSTRAINT scan_events_service_point_id_fkey FOREIGN KEY (service_point_id) REFERENCES medflow.service_points(id);


--
-- Name: scannable_codes scannable_codes_appointment_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.scannable_codes
    ADD CONSTRAINT scannable_codes_appointment_id_fkey FOREIGN KEY (appointment_id) REFERENCES medflow.appointments(id);


--
-- Name: scannable_codes scannable_codes_diagnostic_order_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.scannable_codes
    ADD CONSTRAINT scannable_codes_diagnostic_order_id_fkey FOREIGN KEY (diagnostic_order_id) REFERENCES medflow.diagnostic_orders(id);


--
-- Name: scannable_codes scannable_codes_issued_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.scannable_codes
    ADD CONSTRAINT scannable_codes_issued_by_fkey FOREIGN KEY (issued_by) REFERENCES medflow.users(id);


--
-- Name: scannable_codes scannable_codes_patient_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.scannable_codes
    ADD CONSTRAINT scannable_codes_patient_id_fkey FOREIGN KEY (patient_id) REFERENCES medflow.patients(id);


--
-- Name: scannable_codes scannable_codes_queue_entry_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.scannable_codes
    ADD CONSTRAINT scannable_codes_queue_entry_id_fkey FOREIGN KEY (queue_entry_id) REFERENCES medflow.queue_entries(id);


--
-- Name: service_points service_points_department_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.service_points
    ADD CONSTRAINT service_points_department_id_fkey FOREIGN KEY (department_id) REFERENCES medflow.departments(id);


--
-- Name: service_points service_points_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.service_points
    ADD CONSTRAINT service_points_hospital_id_fkey FOREIGN KEY (hospital_id) REFERENCES medflow.hospitals(id);


--
-- Name: service_points service_points_room_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.service_points
    ADD CONSTRAINT service_points_room_id_fkey FOREIGN KEY (room_id) REFERENCES medflow.rooms(id);


--
-- Name: service_points service_points_service_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.service_points
    ADD CONSTRAINT service_points_service_id_fkey FOREIGN KEY (service_id) REFERENCES medflow.services(id);


--
-- Name: service_prices service_prices_service_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.service_prices
    ADD CONSTRAINT service_prices_service_id_fkey FOREIGN KEY (service_id) REFERENCES medflow.services(id);


--
-- Name: services services_department_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.services
    ADD CONSTRAINT services_department_id_fkey FOREIGN KEY (department_id) REFERENCES medflow.departments(id);


--
-- Name: services services_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.services
    ADD CONSTRAINT services_hospital_id_fkey FOREIGN KEY (hospital_id) REFERENCES medflow.hospitals(id);


--
-- Name: staff staff_department_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.staff
    ADD CONSTRAINT staff_department_id_fkey FOREIGN KEY (department_id) REFERENCES medflow.departments(id);


--
-- Name: staff staff_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.staff
    ADD CONSTRAINT staff_hospital_id_fkey FOREIGN KEY (hospital_id) REFERENCES medflow.hospitals(id);


--
-- Name: staff staff_user_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.staff
    ADD CONSTRAINT staff_user_id_fkey FOREIGN KEY (user_id) REFERENCES medflow.users(id);


--
-- Name: stock_transactions stock_transactions_dispense_item_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.stock_transactions
    ADD CONSTRAINT stock_transactions_dispense_item_id_fkey FOREIGN KEY (dispense_item_id) REFERENCES medflow.dispense_items(id);


--
-- Name: stock_transactions stock_transactions_medicine_batch_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.stock_transactions
    ADD CONSTRAINT stock_transactions_medicine_batch_id_fkey FOREIGN KEY (medicine_batch_id) REFERENCES medflow.medicine_batches(id);


--
-- Name: stock_transactions stock_transactions_performed_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.stock_transactions
    ADD CONSTRAINT stock_transactions_performed_by_fkey FOREIGN KEY (performed_by) REFERENCES medflow.users(id);


--
-- Name: user_roles user_roles_role_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.user_roles
    ADD CONSTRAINT user_roles_role_id_fkey FOREIGN KEY (role_id) REFERENCES medflow.roles(id);


--
-- Name: user_roles user_roles_user_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.user_roles
    ADD CONSTRAINT user_roles_user_id_fkey FOREIGN KEY (user_id) REFERENCES medflow.users(id) ON DELETE CASCADE;


--
-- Name: visit_assignment_history visit_assignment_history_assigned_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_assignment_history
    ADD CONSTRAINT visit_assignment_history_assigned_by_fkey FOREIGN KEY (assigned_by) REFERENCES medflow.users(id);


--
-- Name: visit_assignment_history visit_assignment_history_doctor_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_assignment_history
    ADD CONSTRAINT visit_assignment_history_doctor_id_fkey FOREIGN KEY (doctor_id) REFERENCES medflow.doctors(id);


--
-- Name: visit_assignment_history visit_assignment_history_room_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_assignment_history
    ADD CONSTRAINT visit_assignment_history_room_id_fkey FOREIGN KEY (room_id) REFERENCES medflow.rooms(id);


--
-- Name: visit_assignment_history visit_assignment_history_visit_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_assignment_history
    ADD CONSTRAINT visit_assignment_history_visit_id_fkey FOREIGN KEY (visit_id) REFERENCES medflow.visits(id);


--
-- Name: visit_charges visit_charges_prescription_item_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_charges
    ADD CONSTRAINT visit_charges_prescription_item_id_fkey FOREIGN KEY (prescription_item_id) REFERENCES medflow.prescription_items(id);


--
-- Name: visit_charges visit_charges_service_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_charges
    ADD CONSTRAINT visit_charges_service_id_fkey FOREIGN KEY (service_id) REFERENCES medflow.services(id);


--
-- Name: visit_charges visit_charges_visit_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_charges
    ADD CONSTRAINT visit_charges_visit_id_fkey FOREIGN KEY (visit_id) REFERENCES medflow.visits(id);


--
-- Name: visit_diagnoses visit_diagnoses_recorded_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_diagnoses
    ADD CONSTRAINT visit_diagnoses_recorded_by_fkey FOREIGN KEY (recorded_by) REFERENCES medflow.doctors(id);


--
-- Name: visit_diagnoses visit_diagnoses_visit_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_diagnoses
    ADD CONSTRAINT visit_diagnoses_visit_id_fkey FOREIGN KEY (visit_id) REFERENCES medflow.visits(id);


--
-- Name: visit_vitals visit_vitals_recorded_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_vitals
    ADD CONSTRAINT visit_vitals_recorded_by_fkey FOREIGN KEY (recorded_by) REFERENCES medflow.staff(id);


--
-- Name: visit_vitals visit_vitals_visit_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visit_vitals
    ADD CONSTRAINT visit_vitals_visit_id_fkey FOREIGN KEY (visit_id) REFERENCES medflow.visits(id);


--
-- Name: visits visits_appointment_id_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visits
    ADD CONSTRAINT visits_appointment_id_hospital_id_fkey FOREIGN KEY (appointment_id, hospital_id) REFERENCES medflow.appointments(id, hospital_id);


--
-- Name: visits visits_assigned_doctor_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visits
    ADD CONSTRAINT visits_assigned_doctor_id_fkey FOREIGN KEY (assigned_doctor_id) REFERENCES medflow.doctors(id);


--
-- Name: visits visits_assigned_room_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visits
    ADD CONSTRAINT visits_assigned_room_id_fkey FOREIGN KEY (assigned_room_id) REFERENCES medflow.rooms(id);


--
-- Name: visits visits_created_by_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visits
    ADD CONSTRAINT visits_created_by_fkey FOREIGN KEY (created_by) REFERENCES medflow.users(id);


--
-- Name: visits visits_hospital_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visits
    ADD CONSTRAINT visits_hospital_id_fkey FOREIGN KEY (hospital_id) REFERENCES medflow.hospitals(id);


--
-- Name: visits visits_patient_id_fkey; Type: FK CONSTRAINT; Schema: medflow; Owner: -
--

ALTER TABLE ONLY medflow.visits
    ADD CONSTRAINT visits_patient_id_fkey FOREIGN KEY (patient_id) REFERENCES medflow.patients(id);


--
-- PostgreSQL database dump complete
--

\unrestrict wGTCh8gPLgg296ZEmglqLJv8XL2RG6tEasNGz7isAaQgwFxGficsyw7gaFARlkO

