export interface AuthUser {
  id: string;
  email: string;
  roles: string[];
  permissions?: string[];
}

export interface LoginInput {
  email: string;
  password: string;
  rememberMe?: boolean;
}

export interface RegisterInput {
  email: string;
  password: string;
}
